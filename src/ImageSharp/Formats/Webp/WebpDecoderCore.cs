// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Buffers;
using System.Buffers.Binary;
using SixLabors.ImageSharp.Formats.Webp.Lossless;
using SixLabors.ImageSharp.Formats.Webp.Lossy;
using SixLabors.ImageSharp.IO;
using SixLabors.ImageSharp.Memory;
using SixLabors.ImageSharp.Metadata;
using SixLabors.ImageSharp.Metadata.Profiles.Icc;
using SixLabors.ImageSharp.PixelFormats;

namespace SixLabors.ImageSharp.Formats.Webp;

/// <summary>
/// Performs the webp decoding operation.
/// </summary>
internal sealed class WebpDecoderCore : IImageDecoderInternals, IDisposable
{
    /// <summary>
    /// Reusable buffer.
    /// </summary>
    private readonly byte[] buffer = new byte[4];

    /// <summary>
    /// General configuration options.
    /// </summary>
    private readonly Configuration configuration;

    /// <summary>
    /// A value indicating whether the metadata should be ignored when the image is being decoded.
    /// </summary>
    private readonly bool skipMetadata;

    /// <summary>
    /// The maximum number of frames to decode. Inclusive.
    /// </summary>
    private readonly uint maxFrames;

    /// <summary>
    /// Gets or sets the alpha data, if an ALPH chunk is present.
    /// </summary>
    private IMemoryOwner<byte>? alphaData;

    /// <summary>
    /// Used for allocating memory during the decoding operations.
    /// </summary>
    private readonly MemoryAllocator memoryAllocator;

    /// <summary>
    /// Information about the webp image.
    /// </summary>
    private WebpImageInfo? webImageInfo;

    /// <summary>
    /// Initializes a new instance of the <see cref="WebpDecoderCore"/> class.
    /// </summary>
    /// <param name="options">The decoder options.</param>
    public WebpDecoderCore(DecoderOptions options)
    {
        this.Options = options;
        this.configuration = options.Configuration;
        this.skipMetadata = options.SkipMetadata;
        this.maxFrames = options.MaxFrames;
        this.memoryAllocator = this.configuration.MemoryAllocator;
    }

    /// <inheritdoc/>
    public DecoderOptions Options { get; }

    /// <inheritdoc/>
    public Size Dimensions => new((int)this.webImageInfo!.Width, (int)this.webImageInfo.Height);

    /// <inheritdoc />
    public Image<TPixel> Decode<TPixel>(BufferedReadStream stream, CancellationToken cancellationToken)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        Image<TPixel>? image = null;
        try
        {
            ImageMetadata metadata = new();

            uint fileSize = this.ReadImageHeader(stream);

            using (this.webImageInfo = this.ReadVp8Info(stream, metadata))
            {
                if (this.webImageInfo.Features is { Animation: true })
                {
                    using WebpAnimationDecoder animationDecoder = new(this.memoryAllocator, this.configuration, this.maxFrames);
                    return animationDecoder.Decode<TPixel>(stream, this.webImageInfo.Features, this.webImageInfo.Width, this.webImageInfo.Height, fileSize);
                }

                if (this.webImageInfo.Features is { Animation: true })
                {
                    WebpThrowHelper.ThrowNotSupportedException("Animations are not supported");
                }

                image = new Image<TPixel>(this.configuration, (int)this.webImageInfo.Width, (int)this.webImageInfo.Height, metadata);
                Buffer2D<TPixel> pixels = image.GetRootFramePixelBuffer();
                if (this.webImageInfo.IsLossless)
                {
                    WebpLosslessDecoder losslessDecoder = new(this.webImageInfo.Vp8LBitReader, this.memoryAllocator, this.configuration);
                    losslessDecoder.Decode(pixels, image.Width, image.Height);
                }
                else
                {
                    WebpLossyDecoder lossyDecoder = new(this.webImageInfo.Vp8BitReader, this.memoryAllocator, this.configuration);
                    lossyDecoder.Decode(pixels, image.Width, image.Height, this.webImageInfo, this.alphaData);
                }

                // There can be optional chunks after the image data, like EXIF and XMP.
                if (this.webImageInfo.Features != null)
                {
                    this.ParseOptionalChunks(stream, metadata, this.webImageInfo.Features);
                }

                return image;
            }
        }
        catch
        {
            image?.Dispose();
            throw;
        }
    }

    /// <inheritdoc />
    public ImageInfo Identify(BufferedReadStream stream, CancellationToken cancellationToken)
    {
        this.ReadImageHeader(stream);

        ImageMetadata metadata = new();
        using (this.webImageInfo = this.ReadVp8Info(stream, metadata, true))
        {
            return new ImageInfo(
                new PixelTypeInfo((int)this.webImageInfo.BitsPerPixel),
                new((int)this.webImageInfo.Width, (int)this.webImageInfo.Height),
                metadata);
        }
    }

    /// <summary>
    /// Reads and skips over the image header.
    /// </summary>
    /// <param name="stream">The stream to decode from.</param>
    /// <returns>The file size in bytes.</returns>
    private uint ReadImageHeader(BufferedReadStream stream)
    {
        // Skip FourCC header, we already know its a RIFF file at this point.
        stream.Skip(4);

        // Read file size.
        // The size of the file in bytes starting at offset 8.
        // The file size in the header is the total size of the chunks that follow plus 4 bytes for the ‘WEBP’ FourCC.
        uint fileSize = WebpChunkParsingUtils.ReadChunkSize(stream, this.buffer);

        // Skip 'WEBP' from the header.
        stream.Skip(4);

        return fileSize;
    }

    /// <summary>
    /// Reads information present in the image header, about the image content and how to decode the image.
    /// </summary>
    /// <param name="stream">The stream to decode from.</param>
    /// <param name="metadata">The image metadata.</param>
    /// <param name="ignoreAlpha">For identify, the alpha data should not be read.</param>
    /// <returns>Information about the webp image.</returns>
    private WebpImageInfo ReadVp8Info(BufferedReadStream stream, ImageMetadata metadata, bool ignoreAlpha = false)
    {
        WebpMetadata webpMetadata = metadata.GetFormatMetadata(WebpFormat.Instance);

        WebpChunkType chunkType = WebpChunkParsingUtils.ReadChunkType(stream, this.buffer);

        WebpFeatures features = new();
        switch (chunkType)
        {
            case WebpChunkType.Vp8:
                webpMetadata.FileFormat = WebpFileFormatType.Lossy;
                return WebpChunkParsingUtils.ReadVp8Header(this.memoryAllocator, stream, this.buffer, features);
            case WebpChunkType.Vp8L:
                webpMetadata.FileFormat = WebpFileFormatType.Lossless;
                return WebpChunkParsingUtils.ReadVp8LHeader(this.memoryAllocator, stream, this.buffer, features);
            case WebpChunkType.Vp8X:
                WebpImageInfo webpInfos = WebpChunkParsingUtils.ReadVp8XHeader(stream, this.buffer, features);
                while (stream.Position < stream.Length)
                {
                    chunkType = WebpChunkParsingUtils.ReadChunkType(stream, this.buffer);
                    if (chunkType == WebpChunkType.Vp8)
                    {
                        webpMetadata.FileFormat = WebpFileFormatType.Lossy;
                        webpInfos = WebpChunkParsingUtils.ReadVp8Header(this.memoryAllocator, stream, this.buffer, features);
                    }
                    else if (chunkType == WebpChunkType.Vp8L)
                    {
                        webpMetadata.FileFormat = WebpFileFormatType.Lossless;
                        webpInfos = WebpChunkParsingUtils.ReadVp8LHeader(this.memoryAllocator, stream, this.buffer, features);
                    }
                    else if (WebpChunkParsingUtils.IsOptionalVp8XChunk(chunkType))
                    {
                        bool isAnimationChunk = this.ParseOptionalExtendedChunks(stream, metadata, chunkType, features, ignoreAlpha);
                        if (isAnimationChunk)
                        {
                            return webpInfos;
                        }
                    }
                    else
                    {
                        // Ignore unknown chunks.
                        uint chunkSize = this.ReadChunkSize(stream);
                        stream.Skip((int)chunkSize);
                    }
                }

                return webpInfos;
            default:
                WebpThrowHelper.ThrowImageFormatException("Unrecognized VP8 header");
                return
                    new WebpImageInfo(); // this return will never be reached, because throw helper will throw an exception.
        }
    }

    /// <summary>
    /// Parses optional VP8X chunks, which can be ICCP, XMP, ANIM or ALPH chunks.
    /// </summary>
    /// <param name="stream">The stream to decode from.</param>
    /// <param name="metadata">The image metadata.</param>
    /// <param name="chunkType">The chunk type.</param>
    /// <param name="features">The webp image features.</param>
    /// <param name="ignoreAlpha">For identify, the alpha data should not be read.</param>
    /// <returns>true, if its a alpha chunk.</returns>
    private bool ParseOptionalExtendedChunks(
        BufferedReadStream stream,
        ImageMetadata metadata,
        WebpChunkType chunkType,
        WebpFeatures features,
        bool ignoreAlpha)
    {
        switch (chunkType)
        {
            case WebpChunkType.Iccp:
                this.ReadIccProfile(stream, metadata);
                break;

            case WebpChunkType.Exif:
                this.ReadExifProfile(stream, metadata);
                break;

            case WebpChunkType.Xmp:
                this.ReadXmpProfile(stream, metadata);
                break;

            case WebpChunkType.AnimationParameter:
                this.ReadAnimationParameters(stream, features);
                return true;

            case WebpChunkType.Alpha:
                this.ReadAlphaData(stream, features, ignoreAlpha);
                break;
            default:
                WebpThrowHelper.ThrowImageFormatException("Unexpected chunk followed VP8X header");
                break;
        }

        return false;
    }

    /// <summary>
    /// Reads the optional metadata EXIF of XMP profiles, which can follow the image data.
    /// </summary>
    /// <param name="stream">The stream to decode from.</param>
    /// <param name="metadata">The image metadata.</param>
    /// <param name="features">The webp features.</param>
    private void ParseOptionalChunks(BufferedReadStream stream, ImageMetadata metadata, WebpFeatures features)
    {
        if (this.skipMetadata || (!features.ExifProfile && !features.XmpMetaData))
        {
            return;
        }

        long streamLength = stream.Length;
        while (stream.Position < streamLength)
        {
            // Read chunk header.
            WebpChunkType chunkType = this.ReadChunkType(stream);
            if (chunkType == WebpChunkType.Exif && metadata.ExifProfile == null)
            {
                this.ReadExifProfile(stream, metadata);
            }
            else if (chunkType == WebpChunkType.Xmp && metadata.XmpProfile == null)
            {
                this.ReadXmpProfile(stream, metadata);
            }
            else
            {
                // Skip duplicate XMP or EXIF chunk.
                uint chunkLength = this.ReadChunkSize(stream);
                stream.Skip((int)chunkLength);
            }
        }
    }

    /// <summary>
    /// Reads the EXIF profile from the stream.
    /// </summary>
    /// <param name="stream">The stream to decode from.</param>
    /// <param name="metadata">The image metadata.</param>
    private void ReadExifProfile(BufferedReadStream stream, ImageMetadata metadata)
    {
        uint exifChunkSize = this.ReadChunkSize(stream);
        if (this.skipMetadata)
        {
            stream.Skip((int)exifChunkSize);
        }
        else
        {
            byte[] exifData = new byte[exifChunkSize];
            int bytesRead = stream.Read(exifData, 0, (int)exifChunkSize);
            if (bytesRead != exifChunkSize)
            {
                // Ignore invalid chunk.
                return;
            }

            metadata.ExifProfile = new(exifData);
        }
    }

    /// <summary>
    /// Reads the XMP profile the stream.
    /// </summary>
    /// <param name="stream">The stream to decode from.</param>
    /// <param name="metadata">The image metadata.</param>
    private void ReadXmpProfile(BufferedReadStream stream, ImageMetadata metadata)
    {
        uint xmpChunkSize = this.ReadChunkSize(stream);
        if (this.skipMetadata)
        {
            stream.Skip((int)xmpChunkSize);
        }
        else
        {
            byte[] xmpData = new byte[xmpChunkSize];
            int bytesRead = stream.Read(xmpData, 0, (int)xmpChunkSize);
            if (bytesRead != xmpChunkSize)
            {
                // Ignore invalid chunk.
                return;
            }

            metadata.XmpProfile = new(xmpData);
        }
    }

    /// <summary>
    /// Reads the ICCP chunk from the stream.
    /// </summary>
    /// <param name="stream">The stream to decode from.</param>
    /// <param name="metadata">The image metadata.</param>
    private void ReadIccProfile(BufferedReadStream stream, ImageMetadata metadata)
    {
        uint iccpChunkSize = this.ReadChunkSize(stream);
        if (this.skipMetadata)
        {
            stream.Skip((int)iccpChunkSize);
        }
        else
        {
            byte[] iccpData = new byte[iccpChunkSize];
            int bytesRead = stream.Read(iccpData, 0, (int)iccpChunkSize);
            if (bytesRead != iccpChunkSize)
            {
                WebpThrowHelper.ThrowInvalidImageContentException("Not enough data to read the iccp chunk");
            }

            IccProfile profile = new(iccpData);
            if (profile.CheckIsValid())
            {
                metadata.IccProfile = profile;
            }
        }
    }

    /// <summary>
    /// Reads the animation parameters chunk from the stream.
    /// </summary>
    /// <param name="stream">The stream to decode from.</param>
    /// <param name="features">The webp features.</param>
    private void ReadAnimationParameters(BufferedReadStream stream, WebpFeatures features)
    {
        features.Animation = true;
        uint animationChunkSize = WebpChunkParsingUtils.ReadChunkSize(stream, this.buffer);
        byte blue = (byte)stream.ReadByte();
        byte green = (byte)stream.ReadByte();
        byte red = (byte)stream.ReadByte();
        byte alpha = (byte)stream.ReadByte();
        features.AnimationBackgroundColor = new Color(new Rgba32(red, green, blue, alpha));
        int bytesRead = stream.Read(this.buffer, 0, 2);
        if (bytesRead != 2)
        {
            WebpThrowHelper.ThrowInvalidImageContentException("Not enough data to read the animation loop count");
        }

        features.AnimationLoopCount = BinaryPrimitives.ReadUInt16LittleEndian(this.buffer);
    }

    /// <summary>
    /// Reads the alpha data chunk data from the stream.
    /// </summary>
    /// <param name="stream">The stream to decode from.</param>
    /// <param name="features">The features.</param>
    /// <param name="ignoreAlpha">if set to true, skips the chunk data.</param>
    private void ReadAlphaData(BufferedReadStream stream, WebpFeatures features, bool ignoreAlpha)
    {
        uint alphaChunkSize = WebpChunkParsingUtils.ReadChunkSize(stream, this.buffer);
        if (ignoreAlpha)
        {
            stream.Skip((int)alphaChunkSize);
            return;
        }

        features.AlphaChunkHeader = (byte)stream.ReadByte();
        int alphaDataSize = (int)(alphaChunkSize - 1);
        this.alphaData = this.memoryAllocator.Allocate<byte>(alphaDataSize);
        Span<byte> alphaData = this.alphaData.GetSpan();
        int bytesRead = stream.Read(alphaData, 0, alphaDataSize);
        if (bytesRead != alphaDataSize)
        {
            WebpThrowHelper.ThrowInvalidImageContentException("Not enough data to read the alpha data from the stream");
        }
    }

    /// <summary>
    /// Identifies the chunk type from the chunk.
    /// </summary>
    /// <param name="stream">The stream to decode from.</param>
    /// <exception cref="ImageFormatException">
    /// Thrown if the input stream is not valid.
    /// </exception>
    private WebpChunkType ReadChunkType(BufferedReadStream stream)
    {
        if (stream.Read(this.buffer, 0, 4) == 4)
        {
            return (WebpChunkType)BinaryPrimitives.ReadUInt32BigEndian(this.buffer);
        }

        throw new ImageFormatException("Invalid Webp data.");
    }

    /// <summary>
    /// Reads the chunk size. If Chunk Size is odd, a single padding byte will be added to the payload,
    /// so the chunk size will be increased by 1 in those cases.
    /// </summary>
    /// <param name="stream">The stream to decode from.</param>
    /// <returns>The chunk size in bytes.</returns>
    /// <exception cref="ImageFormatException">Invalid data.</exception>
    private uint ReadChunkSize(BufferedReadStream stream)
    {
        if (stream.Read(this.buffer, 0, 4) == 4)
        {
            uint chunkSize = BinaryPrimitives.ReadUInt32LittleEndian(this.buffer);
            return (chunkSize % 2 == 0) ? chunkSize : chunkSize + 1;
        }

        throw new ImageFormatException("Invalid Webp data.");
    }

    /// <inheritdoc/>
    public void Dispose() => this.alphaData?.Dispose();
}
