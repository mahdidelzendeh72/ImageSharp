// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Buffers;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using SixLabors.ImageSharp.IO;
using SixLabors.ImageSharp.Memory;
using SixLabors.ImageSharp.Metadata;
using SixLabors.ImageSharp.Metadata.Profiles.Xmp;
using SixLabors.ImageSharp.PixelFormats;

namespace SixLabors.ImageSharp.Formats.Gif;

/// <summary>
/// Performs the gif decoding operation.
/// </summary>
internal sealed class GifDecoderCore : IImageDecoderInternals
{
    /// <summary>
    /// The temp buffer used to reduce allocations.
    /// </summary>
    private readonly byte[] buffer = new byte[16];

    /// <summary>
    /// The global color table.
    /// </summary>
    private IMemoryOwner<byte>? globalColorTable;

    /// <summary>
    /// The area to restore.
    /// </summary>
    private Rectangle? restoreArea;

    /// <summary>
    /// The logical screen descriptor.
    /// </summary>
    private GifLogicalScreenDescriptor logicalScreenDescriptor;

    /// <summary>
    /// The graphics control extension.
    /// </summary>
    private GifGraphicControlExtension graphicsControlExtension;

    /// <summary>
    /// The image descriptor.
    /// </summary>
    private GifImageDescriptor imageDescriptor;

    /// <summary>
    /// The global configuration.
    /// </summary>
    private readonly Configuration configuration;

    /// <summary>
    /// Used for allocating memory during processing operations.
    /// </summary>
    private readonly MemoryAllocator memoryAllocator;

    /// <summary>
    /// The maximum number of frames to decode. Inclusive.
    /// </summary>
    private readonly uint maxFrames;

    /// <summary>
    /// Whether to skip metadata during decode.
    /// </summary>
    private readonly bool skipMetadata;

    /// <summary>
    /// The abstract metadata.
    /// </summary>
    private ImageMetadata? metadata;

    /// <summary>
    /// The gif specific metadata.
    /// </summary>
    private GifMetadata? gifMetadata;

    /// <summary>
    /// Initializes a new instance of the <see cref="GifDecoderCore"/> class.
    /// </summary>
    /// <param name="options">The decoder options.</param>
    public GifDecoderCore(DecoderOptions options)
    {
        this.Options = options;
        this.configuration = options.Configuration;
        this.skipMetadata = options.SkipMetadata;
        this.maxFrames = options.MaxFrames;
        this.memoryAllocator = this.configuration.MemoryAllocator;
    }

    /// <inheritdoc />
    public DecoderOptions Options { get; }

    /// <inheritdoc />
    public Size Dimensions => new(this.imageDescriptor.Width, this.imageDescriptor.Height);

    /// <inheritdoc />
    public Image<TPixel> Decode<TPixel>(BufferedReadStream stream, CancellationToken cancellationToken)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        uint frameCount = 0;
        Image<TPixel>? image = null;
        ImageFrame<TPixel>? previousFrame = null;
        try
        {
            this.ReadLogicalScreenDescriptorAndGlobalColorTable(stream);

            // Loop though the respective gif parts and read the data.
            int nextFlag = stream.ReadByte();
            while (nextFlag != GifConstants.Terminator)
            {
                if (nextFlag == GifConstants.ImageLabel)
                {
                    if (previousFrame != null && ++frameCount == this.maxFrames)
                    {
                        break;
                    }

                    this.ReadFrame(stream, ref image, ref previousFrame);

                    // Reset per-frame state.
                    this.imageDescriptor = default;
                    this.graphicsControlExtension = default;
                }
                else if (nextFlag == GifConstants.ExtensionIntroducer)
                {
                    switch (stream.ReadByte())
                    {
                        case GifConstants.GraphicControlLabel:
                            this.ReadGraphicalControlExtension(stream);
                            break;
                        case GifConstants.CommentLabel:
                            this.ReadComments(stream);
                            break;
                        case GifConstants.ApplicationExtensionLabel:
                            this.ReadApplicationExtension(stream);
                            break;
                        case GifConstants.PlainTextLabel:
                            SkipBlock(stream); // Not supported by any known decoder.
                            break;
                    }
                }
                else if (nextFlag == GifConstants.EndIntroducer)
                {
                    break;
                }

                nextFlag = stream.ReadByte();
                if (nextFlag == -1)
                {
                    break;
                }
            }
        }
        finally
        {
            this.globalColorTable?.Dispose();
        }

        if (image is null)
        {
            GifThrowHelper.ThrowNoData();
        }

        return image;
    }

    /// <inheritdoc />
    public ImageInfo Identify(BufferedReadStream stream, CancellationToken cancellationToken)
    {
        uint frameCount = 0;
        ImageFrameMetadata? previousFrame = null;
        List<ImageFrameMetadata> framesMetadata = new();
        try
        {
            this.ReadLogicalScreenDescriptorAndGlobalColorTable(stream);

            // Loop though the respective gif parts and read the data.
            int nextFlag = stream.ReadByte();
            while (nextFlag != GifConstants.Terminator)
            {
                if (nextFlag == GifConstants.ImageLabel)
                {
                    if (previousFrame != null && ++frameCount == this.maxFrames)
                    {
                        break;
                    }

                    this.ReadFrameMetadata(stream, framesMetadata, ref previousFrame);

                    // Reset per-frame state.
                    this.imageDescriptor = default;
                    this.graphicsControlExtension = default;
                }
                else if (nextFlag == GifConstants.ExtensionIntroducer)
                {
                    switch (stream.ReadByte())
                    {
                        case GifConstants.GraphicControlLabel:
                            this.ReadGraphicalControlExtension(stream);
                            break;
                        case GifConstants.CommentLabel:
                            this.ReadComments(stream);
                            break;
                        case GifConstants.ApplicationExtensionLabel:
                            this.ReadApplicationExtension(stream);
                            break;
                        case GifConstants.PlainTextLabel:
                            SkipBlock(stream); // Not supported by any known decoder.
                            break;
                    }
                }
                else if (nextFlag == GifConstants.EndIntroducer)
                {
                    break;
                }

                nextFlag = stream.ReadByte();
                if (nextFlag == -1)
                {
                    break;
                }
            }
        }
        finally
        {
            this.globalColorTable?.Dispose();
        }

        if (this.logicalScreenDescriptor.Width == 0 && this.logicalScreenDescriptor.Height == 0)
        {
            GifThrowHelper.ThrowNoHeader();
        }

        return new ImageInfo(
            new PixelTypeInfo(this.logicalScreenDescriptor.BitsPerPixel),
            new(this.logicalScreenDescriptor.Width, this.logicalScreenDescriptor.Height),
            this.metadata,
            framesMetadata);
    }

    /// <summary>
    /// Reads the graphic control extension.
    /// </summary>
    /// <param name="stream">The <see cref="BufferedReadStream"/> containing image data.</param>
    private void ReadGraphicalControlExtension(BufferedReadStream stream)
    {
        int bytesRead = stream.Read(this.buffer, 0, 6);
        if (bytesRead != 6)
        {
            GifThrowHelper.ThrowInvalidImageContentException("Not enough data to read the graphic control extension");
        }

        this.graphicsControlExtension = GifGraphicControlExtension.Parse(this.buffer);
    }

    /// <summary>
    /// Reads the image descriptor.
    /// </summary>
    /// <param name="stream">The <see cref="BufferedReadStream"/> containing image data.</param>
    private void ReadImageDescriptor(BufferedReadStream stream)
    {
        int bytesRead = stream.Read(this.buffer, 0, 9);
        if (bytesRead != 9)
        {
            GifThrowHelper.ThrowInvalidImageContentException("Not enough data to read the image descriptor");
        }

        this.imageDescriptor = GifImageDescriptor.Parse(this.buffer);
        if (this.imageDescriptor.Height == 0 || this.imageDescriptor.Width == 0)
        {
            GifThrowHelper.ThrowInvalidImageContentException("Width or height should not be 0");
        }
    }

    /// <summary>
    /// Reads the logical screen descriptor.
    /// </summary>
    /// <param name="stream">The <see cref="BufferedReadStream"/> containing image data.</param>
    private void ReadLogicalScreenDescriptor(BufferedReadStream stream)
    {
        int bytesRead = stream.Read(this.buffer, 0, 7);
        if (bytesRead != 7)
        {
            GifThrowHelper.ThrowInvalidImageContentException("Not enough data to read the logical screen descriptor");
        }

        this.logicalScreenDescriptor = GifLogicalScreenDescriptor.Parse(this.buffer);
    }

    /// <summary>
    /// Reads the application extension block parsing any animation or XMP information
    /// if present.
    /// </summary>
    /// <param name="stream">The <see cref="BufferedReadStream"/> containing image data.</param>
    private void ReadApplicationExtension(BufferedReadStream stream)
    {
        int appLength = stream.ReadByte();

        // If the length is 11 then it's a valid extension and most likely
        // a NETSCAPE, XMP or ANIMEXTS extension. We want the loop count from this.
        long position = stream.Position;
        if (appLength == GifConstants.ApplicationBlockSize)
        {
            stream.Read(this.buffer, 0, GifConstants.ApplicationBlockSize);
            bool isXmp = this.buffer.AsSpan().StartsWith(GifConstants.XmpApplicationIdentificationBytes);
            if (isXmp && !this.skipMetadata)
            {
                GifXmpApplicationExtension extension = GifXmpApplicationExtension.Read(stream, this.memoryAllocator);
                if (extension.Data.Length > 0)
                {
                    this.metadata!.XmpProfile = new XmpProfile(extension.Data);
                }
                else
                {
                    // Reset the stream position and continue.
                    stream.Position = position;
                    SkipBlock(stream, appLength);
                }

                return;
            }

            int subBlockSize = stream.ReadByte();

            // TODO: There's also a NETSCAPE buffer extension.
            // http://www.vurdalakov.net/misc/gif/netscape-buffering-application-extension
            if (subBlockSize == GifConstants.NetscapeLoopingSubBlockSize)
            {
                stream.Read(this.buffer, 0, GifConstants.NetscapeLoopingSubBlockSize);
                this.gifMetadata!.RepeatCount = GifNetscapeLoopingApplicationExtension.Parse(this.buffer.AsSpan(1)).RepeatCount;
                stream.Skip(1); // Skip the terminator.
                return;
            }

            // Could be something else not supported yet.
            // Skip the subblock and terminator.
            SkipBlock(stream, subBlockSize);

            return;
        }

        SkipBlock(stream, appLength); // Not supported by any known decoder.
    }

    /// <summary>
    /// Skips over a block or reads its terminator.
    /// </summary>
    /// <param name="stream">The <see cref="BufferedReadStream"/> containing image data.</param>
    /// <param name="blockSize">The length of the block to skip.</param>
    private static void SkipBlock(BufferedReadStream stream, int blockSize = 0)
    {
        if (blockSize > 0)
        {
            stream.Skip(blockSize);
        }

        int flag;

        while ((flag = stream.ReadByte()) > 0)
        {
            stream.Skip(flag);
        }
    }

    /// <summary>
    /// Reads the gif comments.
    /// </summary>
    /// <param name="stream">The <see cref="BufferedReadStream"/> containing image data.</param>
    private void ReadComments(BufferedReadStream stream)
    {
        int length;

        StringBuilder stringBuilder = new();
        while ((length = stream.ReadByte()) != 0)
        {
            if (length > GifConstants.MaxCommentSubBlockLength)
            {
                GifThrowHelper.ThrowInvalidImageContentException($"Gif comment length '{length}' exceeds max '{GifConstants.MaxCommentSubBlockLength}' of a comment data block");
            }

            if (this.skipMetadata)
            {
                stream.Seek(length, SeekOrigin.Current);
                continue;
            }

            using IMemoryOwner<byte> commentsBuffer = this.memoryAllocator.Allocate<byte>(length);
            Span<byte> commentsSpan = commentsBuffer.GetSpan();

            stream.Read(commentsSpan);
            string commentPart = GifConstants.Encoding.GetString(commentsSpan);
            stringBuilder.Append(commentPart);
        }

        if (stringBuilder.Length > 0)
        {
            this.gifMetadata!.Comments.Add(stringBuilder.ToString());
        }
    }

    /// <summary>
    /// Reads an individual gif frame.
    /// </summary>
    /// <typeparam name="TPixel">The pixel format.</typeparam>
    /// <param name="stream">The <see cref="BufferedReadStream"/> containing image data.</param>
    /// <param name="image">The image to decode the information to.</param>
    /// <param name="previousFrame">The previous frame.</param>
    private void ReadFrame<TPixel>(BufferedReadStream stream, ref Image<TPixel>? image, ref ImageFrame<TPixel>? previousFrame)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        this.ReadImageDescriptor(stream);

        IMemoryOwner<byte>? localColorTable = null;
        Buffer2D<byte>? indices = null;
        try
        {
            // Determine the color table for this frame. If there is a local one, use it otherwise use the global color table.
            if (this.imageDescriptor.LocalColorTableFlag)
            {
                int length = this.imageDescriptor.LocalColorTableSize * 3;
                localColorTable = this.configuration.MemoryAllocator.Allocate<byte>(length, AllocationOptions.Clean);
                stream.Read(localColorTable.GetSpan());
            }

            indices = this.configuration.MemoryAllocator.Allocate2D<byte>(this.imageDescriptor.Width, this.imageDescriptor.Height, AllocationOptions.Clean);
            this.ReadFrameIndices(stream, indices);

            Span<byte> rawColorTable = default;
            if (localColorTable != null)
            {
                rawColorTable = localColorTable.GetSpan();
            }
            else if (this.globalColorTable != null)
            {
                rawColorTable = this.globalColorTable.GetSpan();
            }

            ReadOnlySpan<Rgb24> colorTable = MemoryMarshal.Cast<byte, Rgb24>(rawColorTable);
            this.ReadFrameColors(ref image, ref previousFrame, indices, colorTable, this.imageDescriptor);

            // Skip any remaining blocks
            SkipBlock(stream);
        }
        finally
        {
            localColorTable?.Dispose();
            indices?.Dispose();
        }
    }

    /// <summary>
    /// Reads the frame indices marking the color to use for each pixel.
    /// </summary>
    /// <param name="stream">The <see cref="BufferedReadStream"/> containing image data.</param>
    /// <param name="indices">The 2D pixel buffer to write to.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void ReadFrameIndices(BufferedReadStream stream, Buffer2D<byte> indices)
    {
        int minCodeSize = stream.ReadByte();
        using LzwDecoder lzwDecoder = new(this.configuration.MemoryAllocator, stream);
        lzwDecoder.DecodePixels(minCodeSize, indices);
    }

    /// <summary>
    /// Reads the frames colors, mapping indices to colors.
    /// </summary>
    /// <typeparam name="TPixel">The pixel format.</typeparam>
    /// <param name="image">The image to decode the information to.</param>
    /// <param name="previousFrame">The previous frame.</param>
    /// <param name="indices">The indexed pixels.</param>
    /// <param name="colorTable">The color table containing the available colors.</param>
    /// <param name="descriptor">The <see cref="GifImageDescriptor"/></param>
    private void ReadFrameColors<TPixel>(ref Image<TPixel>? image, ref ImageFrame<TPixel>? previousFrame, Buffer2D<byte> indices, ReadOnlySpan<Rgb24> colorTable, in GifImageDescriptor descriptor)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        int imageWidth = this.logicalScreenDescriptor.Width;
        int imageHeight = this.logicalScreenDescriptor.Height;
        bool transFlag = this.graphicsControlExtension.TransparencyFlag;

        ImageFrame<TPixel>? prevFrame = null;
        ImageFrame<TPixel>? currentFrame = null;
        ImageFrame<TPixel> imageFrame;

        if (previousFrame is null)
        {
            if (!transFlag)
            {
                image = new Image<TPixel>(this.configuration, imageWidth, imageHeight, Color.Black.ToPixel<TPixel>(), this.metadata);
            }
            else
            {
                // This initializes the image to become fully transparent because the alpha channel is zero.
                image = new Image<TPixel>(this.configuration, imageWidth, imageHeight, this.metadata);
            }

            this.SetFrameMetadata(image.Frames.RootFrame.Metadata);

            imageFrame = image.Frames.RootFrame;
        }
        else
        {
            if (this.graphicsControlExtension.DisposalMethod == GifDisposalMethod.RestoreToPrevious)
            {
                prevFrame = previousFrame;
            }

            currentFrame = image!.Frames.CreateFrame();

            this.SetFrameMetadata(currentFrame.Metadata);

            imageFrame = currentFrame;

            this.RestoreToBackground(imageFrame);
        }

        if (colorTable.Length == 0)
        {
            return;
        }

        int interlacePass = 0; // The interlace pass
        int interlaceIncrement = 8; // The interlacing line increment
        int interlaceY = 0; // The current interlaced line
        int descriptorTop = descriptor.Top;
        int descriptorBottom = descriptorTop + descriptor.Height;
        int descriptorLeft = descriptor.Left;
        int descriptorRight = descriptorLeft + descriptor.Width;
        byte transIndex = this.graphicsControlExtension.TransparencyIndex;
        int colorTableMaxIdx = colorTable.Length - 1;

        for (int y = descriptorTop; y < descriptorBottom && y < imageHeight; y++)
        {
            ref byte indicesRowRef = ref MemoryMarshal.GetReference(indices.DangerousGetRowSpan(y - descriptorTop));

            // Check if this image is interlaced.
            int writeY; // the target y offset to write to
            if (descriptor.InterlaceFlag)
            {
                // If so then we read lines at predetermined offsets.
                // When an entire image height worth of offset lines has been read we consider this a pass.
                // With each pass the number of offset lines changes and the starting line changes.
                if (interlaceY >= descriptor.Height)
                {
                    interlacePass++;
                    switch (interlacePass)
                    {
                        case 1:
                            interlaceY = 4;
                            break;
                        case 2:
                            interlaceY = 2;
                            interlaceIncrement = 4;
                            break;
                        case 3:
                            interlaceY = 1;
                            interlaceIncrement = 2;
                            break;
                    }
                }

                writeY = interlaceY + descriptor.Top;
                interlaceY += interlaceIncrement;
            }
            else
            {
                writeY = y;
            }

            ref TPixel rowRef = ref MemoryMarshal.GetReference(imageFrame.PixelBuffer.DangerousGetRowSpan(writeY));

            if (!transFlag)
            {
                // #403 The left + width value can be larger than the image width
                for (int x = descriptorLeft; x < descriptorRight && x < imageWidth; x++)
                {
                    int index = Numerics.Clamp(Unsafe.Add(ref indicesRowRef, (uint)(x - descriptorLeft)), 0, colorTableMaxIdx);
                    ref TPixel pixel = ref Unsafe.Add(ref rowRef, (uint)x);
                    Rgb24 rgb = colorTable[index];
                    pixel.FromRgb24(rgb);
                }
            }
            else
            {
                for (int x = descriptorLeft; x < descriptorRight && x < imageWidth; x++)
                {
                    int rawIndex = Unsafe.Add(ref indicesRowRef, (uint)(x - descriptorLeft));

                    // Treat any out of bounds values as transparent.
                    if (rawIndex > colorTableMaxIdx || rawIndex == transIndex)
                    {
                        continue;
                    }

                    int index = Numerics.Clamp(rawIndex, 0, colorTableMaxIdx);
                    ref TPixel pixel = ref Unsafe.Add(ref rowRef, (uint)x);
                    Rgb24 rgb = colorTable[index];
                    pixel.FromRgb24(rgb);
                }
            }
        }

        if (prevFrame != null)
        {
            previousFrame = prevFrame;
            return;
        }

        previousFrame = currentFrame ?? image.Frames.RootFrame;

        if (this.graphicsControlExtension.DisposalMethod == GifDisposalMethod.RestoreToBackground)
        {
            this.restoreArea = new Rectangle(descriptor.Left, descriptor.Top, descriptor.Width, descriptor.Height);
        }
    }

    /// <summary>
    /// Reads the frames metadata.
    /// </summary>
    /// <param name="stream">The <see cref="BufferedReadStream"/> containing image data.</param>
    /// <param name="frameMetadata">The collection of frame metadata.</param>
    /// <param name="previousFrame">The previous frame metadata.</param>
    private void ReadFrameMetadata(BufferedReadStream stream, List<ImageFrameMetadata> frameMetadata, ref ImageFrameMetadata? previousFrame)
    {
        this.ReadImageDescriptor(stream);

        // Skip the color table for this frame if local.
        if (this.imageDescriptor.LocalColorTableFlag)
        {
            stream.Skip(this.imageDescriptor.LocalColorTableSize * 3);
        }

        // Skip the frame indices. Pixels length + mincode size.
        // The gif format does not tell us the length of the compressed data beforehand.
        int minCodeSize = stream.ReadByte();
        using LzwDecoder lzwDecoder = new(this.configuration.MemoryAllocator, stream);
        lzwDecoder.SkipIndices(minCodeSize, this.imageDescriptor.Width * this.imageDescriptor.Height);

        ImageFrameMetadata currentFrame = new();
        frameMetadata.Add(currentFrame);
        this.SetFrameMetadata(currentFrame);
        previousFrame = currentFrame;

        // Skip any remaining blocks
        SkipBlock(stream);
    }

    /// <summary>
    /// Restores the current frame area to the background.
    /// </summary>
    /// <typeparam name="TPixel">The pixel format.</typeparam>
    /// <param name="frame">The frame.</param>
    private void RestoreToBackground<TPixel>(ImageFrame<TPixel> frame)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        if (this.restoreArea is null)
        {
            return;
        }

        Rectangle interest = Rectangle.Intersect(frame.Bounds(), this.restoreArea.Value);
        Buffer2DRegion<TPixel> pixelRegion = frame.PixelBuffer.GetRegion(interest);
        pixelRegion.Clear();

        this.restoreArea = null;
    }

    /// <summary>
    /// Sets the metadata for the image frame.
    /// </summary>
    /// <param name="metadata">The metadata.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void SetFrameMetadata(ImageFrameMetadata metadata)
    {
        // Frames can either use the global table or their own local table.
        if (this.logicalScreenDescriptor.GlobalColorTableFlag
            && this.logicalScreenDescriptor.GlobalColorTableSize > 0)
        {
            GifFrameMetadata gifMeta = metadata.GetGifMetadata();
            gifMeta.ColorTableMode = GifColorTableMode.Global;
            gifMeta.ColorTableLength = this.logicalScreenDescriptor.GlobalColorTableSize;
        }

        if (this.imageDescriptor.LocalColorTableFlag
            && this.imageDescriptor.LocalColorTableSize > 0)
        {
            GifFrameMetadata gifMeta = metadata.GetGifMetadata();
            gifMeta.ColorTableMode = GifColorTableMode.Local;
            gifMeta.ColorTableLength = this.imageDescriptor.LocalColorTableSize;
        }

        // Graphics control extensions is optional.
        if (this.graphicsControlExtension != default)
        {
            GifFrameMetadata gifMeta = metadata.GetGifMetadata();
            gifMeta.FrameDelay = this.graphicsControlExtension.DelayTime;
            gifMeta.DisposalMethod = this.graphicsControlExtension.DisposalMethod;
        }
    }

    /// <summary>
    /// Reads the logical screen descriptor and global color table blocks
    /// </summary>
    /// <param name="stream">The stream containing image data. </param>
    [MemberNotNull(nameof(metadata))]
    [MemberNotNull(nameof(gifMetadata))]
    private void ReadLogicalScreenDescriptorAndGlobalColorTable(BufferedReadStream stream)
    {
        // Skip the identifier
        stream.Skip(6);
        this.ReadLogicalScreenDescriptor(stream);

        ImageMetadata meta = new();

        // The Pixel Aspect Ratio is defined to be the quotient of the pixel's
        // width over its height.  The value range in this field allows
        // specification of the widest pixel of 4:1 to the tallest pixel of
        // 1:4 in increments of 1/64th.
        //
        // Values :        0 -   No aspect ratio information is given.
        //            1..255 -   Value used in the computation.
        //
        // Aspect Ratio = (Pixel Aspect Ratio + 15) / 64
        if (this.logicalScreenDescriptor.PixelAspectRatio > 0)
        {
            meta.ResolutionUnits = PixelResolutionUnit.AspectRatio;
            float ratio = (this.logicalScreenDescriptor.PixelAspectRatio + 15) / 64F;

            if (ratio > 1)
            {
                meta.HorizontalResolution = ratio;
                meta.VerticalResolution = 1;
            }
            else
            {
                meta.VerticalResolution = 1 / ratio;
                meta.HorizontalResolution = 1;
            }
        }

        this.metadata = meta;
        this.gifMetadata = meta.GetGifMetadata();
        this.gifMetadata.ColorTableMode = this.logicalScreenDescriptor.GlobalColorTableFlag
        ? GifColorTableMode.Global
        : GifColorTableMode.Local;

        if (this.logicalScreenDescriptor.GlobalColorTableFlag)
        {
            int globalColorTableLength = this.logicalScreenDescriptor.GlobalColorTableSize * 3;
            this.gifMetadata.GlobalColorTableLength = globalColorTableLength;

            if (globalColorTableLength > 0)
            {
                this.globalColorTable = this.memoryAllocator.Allocate<byte>(globalColorTableLength, AllocationOptions.Clean);

                // Read the global color table data from the stream
                stream.Read(this.globalColorTable.GetSpan());
            }
        }
    }
}
