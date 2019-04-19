﻿using System;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

using BenchmarkDotNet.Attributes;

using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Tuples;

namespace SixLabors.ImageSharp.Benchmarks.General.PixelConversion
{
    //[MonoJob]
    //[RyuJitX64Job]
    public class PixelConversion_Rgba32_To_Bgra32
    {
        private Rgba32[] source;

        private Bgra32[] dest;

        [StructLayout(LayoutKind.Sequential)]
        struct Tuple4OfUInt32
        {
            public uint V0, V1, V2, V3;

            public void ConvertMe()
            {
                this.V0 = FromRgba32.ToBgra32(this.V0);
                this.V1 = FromRgba32.ToBgra32(this.V1);
                this.V2 = FromRgba32.ToBgra32(this.V2);
                this.V3 = FromRgba32.ToBgra32(this.V3);
            }
        }
        
        [Params(64)]
        public int Count { get; set; }

        [GlobalSetup]
        public void Setup()
        {
            this.source = new Rgba32[this.Count];
            this.dest = new Bgra32[this.Count];
        }

        [Benchmark(Baseline = true)]
        public void Default()
        {
            ref Rgba32 sBase = ref this.source[0];
            ref Bgra32 dBase = ref this.dest[0];

            for (int i = 0; i < this.Count; i++)
            {
                ref Rgba32 s = ref Unsafe.Add(ref sBase, i);
                Unsafe.Add(ref dBase, i).FromRgba32(s);
            }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void Default_GenericImpl<TPixel>(ReadOnlySpan<Rgba32> source, Span<TPixel> dest)
            where TPixel : struct, IPixel<TPixel>
        {
            ref Rgba32 sBase = ref MemoryMarshal.GetReference(source);
            ref TPixel dBase = ref MemoryMarshal.GetReference(dest);

            for (int i = 0; i < source.Length; i++)
            {
                ref Rgba32 s = ref Unsafe.Add(ref sBase, i);
                Unsafe.Add(ref dBase, i).FromRgba32(s);
            }
        }

        [Benchmark]
        public void Default_Generic()
        {
            Default_GenericImpl(this.source.AsSpan(), this.dest.AsSpan());
        }

        [Benchmark]
        public void Default_Group2()
        {
            ref Rgba32 sBase = ref this.source[0];
            ref Bgra32 dBase = ref this.dest[0];

            for (int i = 0; i < this.Count; i+=2)
            {
                ref Rgba32 s0 = ref Unsafe.Add(ref sBase, i);
                Rgba32 s1 = Unsafe.Add(ref s0, 1);

                ref Bgra32 d0 = ref Unsafe.Add(ref dBase, i);
                d0.FromRgba32(s0);
                Unsafe.Add(ref d0, 1).FromRgba32(s1);
            }
        }

        [Benchmark]
        public void Default_Group4()
        {
            ref Rgba32 sBase = ref this.source[0];
            ref Bgra32 dBase = ref this.dest[0];

            for (int i = 0; i < this.Count; i += 4)
            {
                ref Rgba32 s0 = ref Unsafe.Add(ref sBase, i);
                ref Rgba32 s1 = ref Unsafe.Add(ref s0, 1);
                ref Rgba32 s2 = ref Unsafe.Add(ref s1, 1);
                Rgba32 s3 = Unsafe.Add(ref s2, 1);

                ref Bgra32 d0 = ref Unsafe.Add(ref dBase, i);
                ref Bgra32 d1 = ref Unsafe.Add(ref d0, 1);
                ref Bgra32 d2 = ref Unsafe.Add(ref d1, 1);

                d0.FromRgba32(s0);
                d1.FromRgba32(s1);
                d2.FromRgba32(s2);
                Unsafe.Add(ref d2, 1).FromRgba32(s3);
            }
        }
        
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void Group4GenericImpl<TPixel>(ReadOnlySpan<Rgba32> source, Span<TPixel> dest)
            where TPixel : struct, IPixel<TPixel>
        {
            ref Rgba32 sBase = ref MemoryMarshal.GetReference(source);
            ref TPixel dBase = ref MemoryMarshal.GetReference(dest);

            for (int i = 0; i < source.Length; i += 4)
            {
                ref Rgba32 s0 = ref Unsafe.Add(ref sBase, i);
                ref Rgba32 s1 = ref Unsafe.Add(ref s0, 1);
                ref Rgba32 s2 = ref Unsafe.Add(ref s1, 1);
                Rgba32 s3 = Unsafe.Add(ref s2, 1);

                ref TPixel d0 = ref Unsafe.Add(ref dBase, i);
                ref TPixel d1 = ref Unsafe.Add(ref d0, 1);
                ref TPixel d2 = ref Unsafe.Add(ref d1, 1);

                d0.FromRgba32(s0);
                d1.FromRgba32(s1);
                d2.FromRgba32(s2);
                Unsafe.Add(ref d2, 1).FromRgba32(s3);
            }
        }

        //[Benchmark]
        public void Default_Group4_Generic()
        {
            Group4GenericImpl(this.source.AsSpan(), this.dest.AsSpan());
        }

        //[Benchmark]
        public void Default_Group8()
        {
            ref Rgba32 sBase = ref this.source[0];
            ref Bgra32 dBase = ref this.dest[0];

            for (int i = 0; i < this.Count / 4; i += 4)
            {
                ref Rgba32 s0 = ref Unsafe.Add(ref sBase, i);
                ref Rgba32 s1 = ref Unsafe.Add(ref s0, 1);
                ref Rgba32 s2 = ref Unsafe.Add(ref s1, 1);
                ref Rgba32 s3 = ref Unsafe.Add(ref s1, 1);

                ref Rgba32 s4 = ref Unsafe.Add(ref s3, 1);
                ref Rgba32 s5 = ref Unsafe.Add(ref s4, 1);
                ref Rgba32 s6 = ref Unsafe.Add(ref s5, 1);
                Rgba32 s7 = Unsafe.Add(ref s6, 1);

                ref Bgra32 d0 = ref Unsafe.Add(ref dBase, i);
                ref Bgra32 d1 = ref Unsafe.Add(ref d0, 1);
                ref Bgra32 d2 = ref Unsafe.Add(ref d1, 1);
                ref Bgra32 d3 = ref Unsafe.Add(ref d2, 1);
                ref Bgra32 d4 = ref Unsafe.Add(ref d3, 1);

                ref Bgra32 d5 = ref Unsafe.Add(ref d4, 1);
                ref Bgra32 d6 = ref Unsafe.Add(ref d5, 1);


                d0.FromRgba32(s0);
                d1.FromRgba32(s1);
                d2.FromRgba32(s2);
                d3.FromRgba32(s3);

                d4.FromRgba32(s4);
                d5.FromRgba32(s5);
                d6.FromRgba32(s6);
                Unsafe.Add(ref d6, 1).FromRgba32(s7);
            }
        }

        [Benchmark]
        public void BitOps()
        {
            ref uint sBase = ref Unsafe.As<Rgba32, uint>(ref this.source[0]);
            ref uint dBase = ref Unsafe.As<Bgra32, uint>(ref this.dest[0]);

            for (int i = 0; i < this.Count; i++)
            {
                uint s = Unsafe.Add(ref sBase, i);
                Unsafe.Add(ref dBase, i) = FromRgba32.ToBgra32(s);
            }
        }

        [Benchmark]
        public void Bitops_Tuple()
        {
            ref Tuple4OfUInt32 sBase = ref Unsafe.As<Rgba32, Tuple4OfUInt32>(ref this.source[0]);
            ref Tuple4OfUInt32 dBase = ref Unsafe.As<Bgra32, Tuple4OfUInt32>(ref this.dest[0]);

            for (int i = 0; i < this.Count / 4; i++)
            {
                ref Tuple4OfUInt32 d = ref Unsafe.Add(ref dBase, i);
                d = Unsafe.Add(ref sBase, i);
                d.ConvertMe();
            }
        }

        //[Benchmark]
        public void Bitops_SingleTuple()
        {
            ref Tuple4OfUInt32 sBase = ref Unsafe.As<Rgba32, Tuple4OfUInt32>(ref this.source[0]);

            for (int i = 0; i < this.Count / 4; i++)
            {
                Unsafe.Add(ref sBase, i).ConvertMe();
            }
        }

        //[Benchmark]
        public void Bitops_Simd()
        {
            ref Octet.OfUInt32 sBase = ref Unsafe.As<Rgba32, Octet.OfUInt32>(ref this.source[0]);
            ref Octet.OfUInt32 dBase = ref Unsafe.As<Bgra32, Octet.OfUInt32>(ref this.dest[0]);

            for (int i = 0; i < this.Count / 8; i++)
            {
                BitopsSimdImpl(ref Unsafe.Add(ref sBase, i), ref Unsafe.Add(ref dBase, i));
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        struct B
        {
            public uint tmp2, tmp5, tmp8, tmp11, tmp14, tmp17, tmp20, tmp23;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct C
        {
            public uint tmp3, tmp6, tmp9, tmp12, tmp15, tmp18, tmp21, tmp24;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void BitopsSimdImpl(ref Octet.OfUInt32 s, ref Octet.OfUInt32 d)
        {
            Vector<uint> sVec = Unsafe.As<Octet.OfUInt32, Vector<uint>>(ref s);
            Vector<uint> aMask = new Vector<uint>(0xFF00FF00);
            Vector<uint> bMask = new Vector<uint>(0x00FF00FF);

            Vector<uint> aa = sVec & aMask;
            Vector<uint> bb = sVec & bMask;

            B b = Unsafe.As<Vector<uint>, B>(ref bb);

            C c = default;

            c.tmp3 = (b.tmp2 << 16) | (b.tmp2 >> 16);
            c.tmp6 = (b.tmp5 << 16) | (b.tmp5 >> 16);
            c.tmp9 = (b.tmp8 << 16) | (b.tmp8 >> 16);
            c.tmp12 = (b.tmp11 << 16) | (b.tmp11 >> 16);
            c.tmp15 = (b.tmp14 << 16) | (b.tmp14 >> 16);
            c.tmp18 = (b.tmp17 << 16) | (b.tmp17 >> 16);
            c.tmp21 = (b.tmp20 << 16) | (b.tmp20 >> 16);
            c.tmp24 = (b.tmp23 << 16) | (b.tmp23 >> 16);

            Vector<uint> cc = Unsafe.As<C, Vector<uint>>(ref c);
            Vector<uint> dd = aa + cc;

            d = Unsafe.As<Vector<uint>, Octet.OfUInt32>(ref dd);
        }

        //[Benchmark]
        public void BitOps_Group2()
        {
            ref uint sBase = ref Unsafe.As<Rgba32, uint>(ref this.source[0]);
            ref uint dBase = ref Unsafe.As<Bgra32, uint>(ref this.dest[0]);

            for (int i = 0; i < this.Count; i++)
            {
                ref uint s0 = ref Unsafe.Add(ref sBase, i);
                uint s1 = Unsafe.Add(ref s0, 1);

                ref uint d0 = ref Unsafe.Add(ref dBase, i);
                d0 = FromRgba32.ToBgra32(s0);
                Unsafe.Add(ref d0, 1) = FromRgba32.ToBgra32(s1);
            }
        }
        
        [Benchmark]
        public void BitOps_GroupAsULong()
        {
            ref ulong sBase = ref Unsafe.As<Rgba32, ulong>(ref this.source[0]);
            ref ulong dBase = ref Unsafe.As<Bgra32, ulong>(ref this.dest[0]);

            for (int i = 0; i < this.Count / 2; i++)
            {
                ulong s = Unsafe.Add(ref sBase, i);
                uint lo = (uint)s;
                uint hi = (uint)(s >> 32);
                lo = FromRgba32.ToBgra32(lo);
                hi = FromRgba32.ToBgra32(hi);

                s = (ulong)(hi << 32) | lo;

                Unsafe.Add(ref dBase, i) = s;
            }
        }

        //[Benchmark]
        public void BitOps_GroupAsULong_V2()
        {
            ref ulong sBase = ref Unsafe.As<Rgba32, ulong>(ref this.source[0]);
            ref ulong dBase = ref Unsafe.As<Bgra32, ulong>(ref this.dest[0]);

            for (int i = 0; i < this.Count / 2; i++)
            {
                ulong s = Unsafe.Add(ref sBase, i);
                uint lo = (uint)s;
                uint hi = (uint)(s >> 32);

                uint tmp1 = lo & 0xFF00FF00;
                uint tmp4 = hi & 0xFF00FF00;

                uint tmp2 = lo & 0x00FF00FF;
                uint tmp5 = hi & 0x00FF00FF;

                uint tmp3 = (tmp2 << 16) | (tmp2 >> 16);
                uint tmp6 = (tmp5 << 16) | (tmp5 >> 16);

                lo = tmp1 + tmp3;
                hi = tmp4 + tmp6;

                s = (ulong)(hi << 32) | lo;

                Unsafe.Add(ref dBase, i) = s;
            }
        }

        public static class FromRgba32
        {
            /// <summary>
            /// Converts a packed <see cref="Rgba32"/> to <see cref="Argb32"/>.
            /// </summary>
            [MethodImpl(InliningOptions.ShortMethod)]
            public static uint ToArgb32(uint packedRgba)
            {
                // packedRgba          = [aa bb gg rr]
                // ROL(8, packedRgba)  = [bb gg rr aa]
                return (packedRgba << 8) | (packedRgba >> 24);
            }

            /// <summary>
            /// Converts a packed <see cref="Rgba32"/> to <see cref="Bgra32"/>.
            /// </summary>
            [MethodImpl(InliningOptions.ShortMethod)]
            public static uint ToBgra32(uint packedRgba)
            {
                // packedRgba          = [aa bb gg rr]
                // tmp1                = [aa 00 gg 00]
                // tmp2                = [00 bb 00 rr]
                // tmp3=ROL(16, tmp2)  = [00 rr 00 bb]
                // tmp1 + tmp3         = [aa rr gg bb]
                uint tmp1 = packedRgba & 0xFF00FF00;
                uint tmp2 = packedRgba & 0x00FF00FF;
                uint tmp3 = (tmp2 << 16) | (tmp2 >> 16);
                return tmp1 + tmp3;
            }
        }


        // RESULTS:
        //               Method | Count |     Mean |     Error |    StdDev | Scaled | ScaledSD |
        // -------------------- |------ |---------:|----------:|----------:|-------:|---------:|
        //              Default |    64 | 82.67 ns | 0.6737 ns | 0.5625 ns |   1.00 |     0.00 |
        //      Default_Generic |    64 | 88.73 ns | 1.7959 ns | 1.7638 ns |   1.07 |     0.02 |
        //       Default_Group2 |    64 | 91.03 ns | 1.5237 ns | 1.3508 ns |   1.10 |     0.02 |
        //       Default_Group4 |    64 | 86.62 ns | 1.5737 ns | 1.4720 ns |   1.05 |     0.02 |
        //               BitOps |    64 | 57.45 ns | 0.6067 ns | 0.5066 ns |   0.69 |     0.01 |
        //         Bitops_Tuple |    64 | 75.47 ns | 1.1824 ns | 1.1060 ns |   0.91 |     0.01 |
        //  BitOps_GroupAsULong |    64 | 65.42 ns | 0.7157 ns | 0.6695 ns |   0.79 |     0.01 |
    }
}