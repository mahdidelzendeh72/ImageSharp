﻿// Copyright (c) Six Labors and contributors.
// Licensed under the Apache License, Version 2.0.

using System.Diagnostics;

using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.Formats.Jpeg.Common;
using SixLabors.ImageSharp.Formats.Jpeg.GolangPort.Components;
using SixLabors.ImageSharp.Formats.Jpeg.GolangPort.Utils;

using Xunit;
using Xunit.Abstractions;

// Uncomment this to turn unit tests into benchmarks:
//#define BENCHMARKING

// ReSharper disable InconsistentNaming

namespace SixLabors.ImageSharp.Tests
{
    using System;

    public class Block8x8FTests : JpegUtilityTestFixture
    {
#if BENCHMARKING
        public const int Times = 1000000;
#else
        public const int Times = 1;
#endif

        public Block8x8FTests(ITestOutputHelper output)
            : base(output)
        {
        }

        [Fact]
        public void Indexer()
        {
            float sum = 0;
            this.Measure(
                Times,
                () =>
                    {
                        Block8x8F block = new Block8x8F();

                        for (int i = 0; i < Block8x8F.Size; i++)
                        {
                            block[i] = i;
                        }

                        sum = 0;
                        for (int i = 0; i < Block8x8F.Size; i++)
                        {
                            sum += block[i];
                        }
                    });
            Assert.Equal(sum, 64f * 63f * 0.5f);
        }

        [Fact]
        public unsafe void Indexer_GetScalarAt_SetScalarAt()
        {
            float sum = 0;
            this.Measure(
                Times,
                () =>
                    {
                        Block8x8F block = new Block8x8F();

                        for (int i = 0; i < Block8x8F.Size; i++)
                        {
                            Block8x8F.SetScalarAt(&block, i, i);
                        }

                        sum = 0;
                        for (int i = 0; i < Block8x8F.Size; i++)
                        {
                            sum += Block8x8F.GetScalarAt(&block, i);
                        }
                    });
            Assert.Equal(sum, 64f * 63f * 0.5f);
        }

        [Fact]
        public void Indexer_ReferenceBenchmarkWithArray()
        {
            float sum = 0;

            this.Measure(
                Times,
                () =>
                    {
                        // Block8x8F block = new Block8x8F();
                        float[] block = new float[64];
                        for (int i = 0; i < Block8x8F.Size; i++)
                        {
                            block[i] = i;
                        }

                        sum = 0;
                        for (int i = 0; i < Block8x8F.Size; i++)
                        {
                            sum += block[i];
                        }
                    });
            Assert.Equal(sum, 64f * 63f * 0.5f);
        }

        [Fact]
        public void Load_Store_FloatArray()
        {
            float[] data = new float[Block8x8F.Size];
            float[] mirror = new float[Block8x8F.Size];

            for (int i = 0; i < Block8x8F.Size; i++)
            {
                data[i] = i;
            }

            this.Measure(
                Times,
                () =>
                    {
                        Block8x8F b = new Block8x8F();
                        b.LoadFrom(data);
                        b.CopyTo(mirror);
                    });

            Assert.Equal(data, mirror);

            // PrintLinearData((Span<float>)mirror);
        }

        [Fact]
        public unsafe void Load_Store_FloatArray_Ptr()
        {
            float[] data = new float[Block8x8F.Size];
            float[] mirror = new float[Block8x8F.Size];

            for (int i = 0; i < Block8x8F.Size; i++)
            {
                data[i] = i;
            }

            this.Measure(
                Times,
                () =>
                    {
                        Block8x8F b = new Block8x8F();
                        Block8x8F.LoadFrom(&b, data);
                        Block8x8F.CopyTo(&b, mirror);
                    });

            Assert.Equal(data, mirror);

            // PrintLinearData((Span<float>)mirror);
        }

        [Fact]
        public void Load_Store_IntArray()
        {
            int[] data = new int[Block8x8F.Size];
            int[] mirror = new int[Block8x8F.Size];

            for (int i = 0; i < Block8x8F.Size; i++)
            {
                data[i] = i;
            }

            this.Measure(
                Times,
                () =>
                    {
                        Block8x8F v = new Block8x8F();
                        v.LoadFrom(data);
                        v.CopyTo(mirror);
                    });

            Assert.Equal(data, mirror);

            // PrintLinearData((Span<int>)mirror);
        }

        [Fact]
        public void TransposeInto()
        {
            float[] expected = Create8x8FloatData();
            ReferenceImplementations.Transpose8x8(expected);

            Block8x8F source = new Block8x8F();
            source.LoadFrom(Create8x8FloatData());

            Block8x8F dest = new Block8x8F();
            source.TransposeInto(ref dest);

            float[] actual = new float[64];
            dest.CopyTo(actual);

            Assert.Equal(expected, actual);
        }

        private class BufferHolder
        {
            public Block8x8F Buffer;
        }

        [Fact]
        public void TranposeInto_Benchmark()
        {
            BufferHolder source = new BufferHolder();
            source.Buffer.LoadFrom(Create8x8FloatData());
            BufferHolder dest = new BufferHolder();

            this.Output.WriteLine($"TranposeInto_PinningImpl_Benchmark X {Times} ...");
            Stopwatch sw = Stopwatch.StartNew();

            for (int i = 0; i < Times; i++)
            {
                source.Buffer.TransposeInto(ref dest.Buffer);
            }

            sw.Stop();
            this.Output.WriteLine($"TranposeInto_PinningImpl_Benchmark finished in {sw.ElapsedMilliseconds} ms");
        }

        [Fact]
        public void iDCT2D8x4_LeftPart()
        {
            float[] sourceArray = Create8x8FloatData();
            float[] expectedDestArray = new float[64];

            ReferenceImplementations.iDCT2D8x4_32f(sourceArray, expectedDestArray);

            Block8x8F source = new Block8x8F();
            source.LoadFrom(sourceArray);

            Block8x8F dest = new Block8x8F();

            DCT.IDCT8x4_LeftPart(ref source, ref dest);

            float[] actualDestArray = new float[64];
            dest.CopyTo(actualDestArray);

            this.Print8x8Data(expectedDestArray);
            this.Output.WriteLine("**************");
            this.Print8x8Data(actualDestArray);

            Assert.Equal(expectedDestArray, actualDestArray);
        }

        [Fact]
        public void iDCT2D8x4_RightPart()
        {
            float[] sourceArray = Create8x8FloatData();
            float[] expectedDestArray = new float[64];

            ReferenceImplementations.iDCT2D8x4_32f(sourceArray.AsSpan().Slice(4), expectedDestArray.AsSpan().Slice(4));

            Block8x8F source = new Block8x8F();
            source.LoadFrom(sourceArray);

            Block8x8F dest = new Block8x8F();

            DCT.IDCT8x4_RightPart(ref source, ref dest);

            float[] actualDestArray = new float[64];
            dest.CopyTo(actualDestArray);

            this.Print8x8Data(expectedDestArray);
            this.Output.WriteLine("**************");
            this.Print8x8Data(actualDestArray);

            Assert.Equal(expectedDestArray, actualDestArray);
        }

        [Theory]
        [InlineData(1)]
        [InlineData(2)]
        [InlineData(3)]
        public void TransformIDCT(int seed)
        {
            Span<float> sourceArray = Create8x8RandomFloatData(-200, 200, seed);
            float[] expectedDestArray = new float[64];
            float[] tempArray = new float[64];

            ReferenceImplementations.iDCT2D_llm(sourceArray, expectedDestArray, tempArray);

            // ReferenceImplementations.iDCT8x8_llm_sse(sourceArray, expectedDestArray, tempArray);
            Block8x8F source = new Block8x8F();
            source.LoadFrom(sourceArray);

            Block8x8F dest = new Block8x8F();
            Block8x8F tempBuffer = new Block8x8F();

            DCT.TransformIDCT(ref source, ref dest, ref tempBuffer);

            float[] actualDestArray = new float[64];
            dest.CopyTo(actualDestArray);

            this.Print8x8Data(expectedDestArray);
            this.Output.WriteLine("**************");
            this.Print8x8Data(actualDestArray);
            Assert.Equal(expectedDestArray, actualDestArray, new ApproximateFloatComparer(1f));
            Assert.Equal(expectedDestArray, actualDestArray, new ApproximateFloatComparer(1f));
        }

        [Fact]
        public unsafe void CopyColorsTo()
        {
            float[] data = Create8x8FloatData();
            Block8x8F block = new Block8x8F();
            block.LoadFrom(data);
            block.MultiplyAllInplace(5);

            int stride = 256;
            int height = 42;
            int offset = height * 10 + 20;

            byte[] colorsExpected = new byte[stride * height];
            byte[] colorsActual = new byte[stride * height];

            Block8x8F temp = new Block8x8F();

            ReferenceImplementations.CopyColorsTo(ref block, new Span<byte>(colorsExpected, offset), stride);

            block.CopyColorsTo(new Span<byte>(colorsActual, offset), stride, &temp);

            // Output.WriteLine("******* EXPECTED: *********");
            // PrintLinearData(colorsExpected);
            // Output.WriteLine("******** ACTUAL: **********");
            Assert.Equal(colorsExpected, colorsActual);
        }

        private static float[] Create8x8ColorCropTestData()
        {
            float[] result = new float[64];
            for (int i = 0; i < 8; i++)
            {
                for (int j = 0; j < 8; j++)
                {
                    result[i * 8 + j] = -300 + i * 100 + j * 10;
                }
            }

            return result;
        }

        [Fact]
        public void TransformByteConvetibleColorValuesInto()
        {
            Block8x8F block = new Block8x8F();
            float[] input = Create8x8ColorCropTestData();
            block.LoadFrom(input);
            this.Output.WriteLine("Input:");
            this.PrintLinearData(input);

            Block8x8F dest = new Block8x8F();
            block.TransformByteConvetibleColorValuesInto(ref dest);

            float[] array = new float[64];
            dest.CopyTo(array);
            this.Output.WriteLine("Result:");
            this.PrintLinearData(array);
            foreach (float val in array)
            {
                Assert.InRange(val, 0, 255);
            }
        }

        [Theory]
        [InlineData(1)]
        [InlineData(2)]
        public void FDCT8x4_LeftPart(int seed)
        {
            Span<float> src = Create8x8RandomFloatData(-200, 200, seed);
            Block8x8F srcBlock = new Block8x8F();
            srcBlock.LoadFrom(src);

            Block8x8F destBlock = new Block8x8F();

            float[] expectedDest = new float[64];

            ReferenceImplementations.fDCT2D8x4_32f(src, expectedDest);
            DCT.FDCT8x4_LeftPart(ref srcBlock, ref destBlock);

            float[] actualDest = new float[64];
            destBlock.CopyTo(actualDest);

            Assert.Equal(actualDest, expectedDest, new ApproximateFloatComparer(1f));
        }

        [Theory]
        [InlineData(1)]
        [InlineData(2)]
        public void FDCT8x4_RightPart(int seed)
        {
            Span<float> src = Create8x8RandomFloatData(-200, 200, seed);
            Block8x8F srcBlock = new Block8x8F();
            srcBlock.LoadFrom(src);

            Block8x8F destBlock = new Block8x8F();

            float[] expectedDest = new float[64];

            ReferenceImplementations.fDCT2D8x4_32f(src.Slice(4), expectedDest.AsSpan().Slice(4));
            DCT.FDCT8x4_RightPart(ref srcBlock, ref destBlock);

            float[] actualDest = new float[64];
            destBlock.CopyTo(actualDest);

            Assert.Equal(actualDest, expectedDest, new ApproximateFloatComparer(1f));
        }

        [Theory]
        [InlineData(1)]
        [InlineData(2)]
        public void TransformFDCT(int seed)
        {
            Span<float> src = Create8x8RandomFloatData(-200, 200, seed);
            Block8x8F srcBlock = new Block8x8F();
            srcBlock.LoadFrom(src);

            Block8x8F destBlock = new Block8x8F();

            float[] expectedDest = new float[64];
            float[] temp1 = new float[64];
            Block8x8F temp2 = new Block8x8F();

            ReferenceImplementations.fDCT2D_llm(src, expectedDest, temp1, downscaleBy8: true);
            DCT.TransformFDCT(ref srcBlock, ref destBlock, ref temp2, false);

            float[] actualDest = new float[64];
            destBlock.CopyTo(actualDest);

            Assert.Equal(actualDest, expectedDest, new ApproximateFloatComparer(1f));
        }

        [Theory]
        [InlineData(1)]
        [InlineData(2)]
        public unsafe void UnzigDivRound(int seed)
        {
            Block8x8F block = new Block8x8F();
            block.LoadFrom(Create8x8RandomFloatData(-2000, 2000, seed));

            Block8x8F qt = new Block8x8F();
            qt.LoadFrom(Create8x8RandomFloatData(-2000, 2000, seed));

            UnzigData unzig = UnzigData.Create();

            int* expectedResults = stackalloc int[Block8x8F.Size];
            ReferenceImplementations.UnZigDivRoundRational(&block, expectedResults, &qt, unzig.Data);

            Block8x8F actualResults = default(Block8x8F);

            Block8x8F.UnzigDivRound(&block, &actualResults, &qt, unzig.Data);

            for (int i = 0; i < Block8x8F.Size; i++)
            {
                int expected = expectedResults[i];
                int actual = (int)actualResults[i];

                Assert.Equal(expected, actual);
            }
        }

        [Fact]
        public void AsInt16Block()
        {
            float[] data = Create8x8FloatData();

            var source = default(Block8x8F);
            source.LoadFrom(data);

            Block8x8 dest = source.AsInt16Block();

            for (int i = 0; i < Block8x8F.Size; i++)
            {
                Assert.Equal((short)data[i], dest[i]);
            }
        }
    }
}