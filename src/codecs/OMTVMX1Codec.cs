/*
* MIT License
*
* Copyright (c) 2025 Open Media Transport Contributors
*
* Permission is hereby granted, free of charge, to any person obtaining a copy
* of this software and associated documentation files (the "Software"), to deal
* in the Software without restriction, including without limitation the rights
* to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
* copies of the Software, and to permit persons to whom the Software is
* furnished to do so, subject to the following conditions:
*
* The above copyright notice and this permission notice shall be included in all
* copies or substantial portions of the Software.
*
* THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
* IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
* FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
* AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
* LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
* OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
* SOFTWARE.
*
*/

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security;
using System.Text;
using libomtnet;

namespace libomtnet.codecs
{
    /// <summary>
    /// VMX Profile sets bitrate targets and DC coding precision
    /// HQ, SQ, LQ: VMX Legacy recording profiles
    /// OMT Profiles (SQ Default):
    /// 2160p60 HQ 600 SQ 300 LQ 200 Mbps
    /// 1080p60 HQ 288 SQ 200 LQ 80 Mbps
    /// SD HQ 72 SQ 36 LQ 24 Mbps
    /// </summary>
    public enum VMXProfile
    {
        None = 0,
        Default = 1,
        LQ = 33,
        SQ = 66,
        HQ = 99,
        OMT_LQ = 133,
        OMT_SQ = 166,
        OMT_HQ = 199
    }

    public enum VMXColorSpace
    {
        Undefined = 0,
        BT601 = 601,
        BT709 = 709
    }

    public enum VMXImageType
    {
        None = 0,
        UYVY = 1,
        YUY2 = 2,
        NV12 = 3,
        YV12 = 4,
        BGRA = 5,
        BGRX = 6,
        UYVA = 7,
        P216 = 8,
        PA16 = 9
    }

    /// <summary>
    /// VMX Encoder/Decoder for OMT
    /// </summary>
    public class OMTVMX1Codec : OMTBase
    {
        private const string DLLPATH = @"libvmx";
        private readonly int width;
        private readonly int height;
        private readonly int framesPerSecond;
        private readonly VMXProfile profile;
        private readonly VMXColorSpace colorSpace;
        private IntPtr instance;
        private IVMXCodec codec;

        public OMTVMX1Codec(int width, int height, int framesPerSecond, VMXProfile profile = VMXProfile.Default, VMXColorSpace colorSpace = VMXColorSpace.Undefined)
        {            
            if (OMTPlatform.GetPlatformType() == OMTPlatformType.iOS)
            {
                codec = new VMXCodecIOS();
            } else
            {
                codec = new VMXCodec();
            }

            this.width = width;
            this.height = height;
            this.profile = profile;
            this.colorSpace = colorSpace;
            this.framesPerSecond = framesPerSecond;
            if (profile == VMXProfile.Default) { profile = VMXProfile.OMT_SQ; }
            this.instance = codec.VMX_Create(new OMTSize(width, height), profile, colorSpace);
            if (framesPerSecond > 60)
            {
                int threads = codec.VMX_GetThreads(this.instance);
                threads *= 2;
                codec.VMX_SetThreads(this.instance, threads);
                Debug.WriteLine("Codec.SetThreads: " + threads);
            }
            ApplyEnhancedQualityProfile();
        }

        private enum EnhancedQualityProfileMode
        {
            Safe,
            Max
        }

        private void ApplyEnhancedQualityProfile()
        {
            try
            {
                OMTSettings settings = OMTSettings.GetInstance();
                bool enabled = settings.GetBoolean("EnhancedQualityEnabled", false);
                string modeString = settings.GetEnhancedQualityMode("Safe");
                EnhancedQualityProfileMode mode = EnhancedQualityProfileMode.Safe;
                if (modeString != null && modeString.Equals("Max", StringComparison.OrdinalIgnoreCase))
                {
                    mode = EnhancedQualityProfileMode.Max;
                }

                if (!enabled)
                {
                    OMTLogging.Write("EnhancedQuality disabled", "OMTVMX1Codec");
                    return;
                }

                int frameMin, frameMax, minQuality, dcShift;
                codec.VMX_GetEncodingParameters(this.instance, out frameMin, out frameMax, out minQuality, out dcShift);
                int oldFrameMin = frameMin;
                int oldFrameMax = frameMax;
                int oldMinQuality = minQuality;
                int oldDcShift = dcShift;

                int newFrameMax = frameMax;
                int newMinQuality = minQuality;
                int newQuality = GetQuality();

                if (mode == EnhancedQualityProfileMode.Max)
                {
                    newFrameMax = 12 * 1024 * 1024;
                    newMinQuality = 96;
                    newQuality = 99;
                }
                else
                {
                    newFrameMax = 8 * 1024 * 1024;
                    newMinQuality = 92;
                    newQuality = 96;
                }

                codec.VMX_SetEncodingParameters(this.instance, frameMin, newFrameMax, newMinQuality, dcShift);
                codec.VMX_SetQuality(this.instance, newQuality);

                int verifiedFrameMin, verifiedFrameMax, verifiedMinQuality, verifiedDcShift;
                codec.VMX_GetEncodingParameters(this.instance, out verifiedFrameMin, out verifiedFrameMax, out verifiedMinQuality, out verifiedDcShift);
                int verifiedQuality = codec.VMX_GetQuality(this.instance);

                OMTLogging.Write(string.Format("EnhancedQuality enabled, mode={0}, old frameMin={1}, old frameMax={2}, old minQuality={3}, old dcShift={4}, new frameMin={5}, new frameMax={6}, new minQuality={7}, new dcShift={8}, quality={9}, reportedQuality={10}",
                    mode, oldFrameMin, oldFrameMax, oldMinQuality, oldDcShift,
                    verifiedFrameMin, verifiedFrameMax, verifiedMinQuality, verifiedDcShift,
                    newQuality, verifiedQuality), "OMTVMX1Codec");
            }
            catch (Exception ex)
            {
                OMTLogging.Write("EnhancedQuality setup failed: " + ex.Message, "OMTVMX1Codec");
            }
        }

        public float CalculatePSNR(byte[] image1, byte[] image2, int stride, int bytesPerPixel, OMTSize sz)
        {
            return codec.VMX_CalculatePSNR(image1, image2, stride, bytesPerPixel, sz);
        }

        public void SetQuality(int quality)
        {
            codec.VMX_SetQuality(this.instance, quality);
        }

        public int GetQuality()
        {
            return codec.VMX_GetQuality(this.instance);
        }

        public int Encode(VMXImageType itype, IntPtr src, int srcStride, byte[] dst, bool interlaced)
        {
            int i = 0;
            if (interlaced) i = 1;
            int hr = 0;
            switch (itype)
            {
                case VMXImageType.UYVY:
                    hr = codec.VMX_EncodeUYVY(instance, src, srcStride, i);
                    break;
                case VMXImageType.UYVA:
                    hr = codec.VMX_EncodeUYVA(instance, src, srcStride, i);
                    break;
                case VMXImageType.P216:
                    hr = codec.VMX_EncodeP216(instance, src, srcStride, i);
                    break;
                case VMXImageType.PA16:
                    hr = codec.VMX_EncodePA16(instance, src, srcStride, i);
                    break;
                case VMXImageType.YUY2:
                    hr = codec.VMX_EncodeYUY2(instance, src, srcStride, i);
                    break;
                case VMXImageType.NV12:
                    IntPtr srcUV = src + (srcStride * height);
                    hr = codec.VMX_EncodeNV12(instance, src, srcStride, srcUV, srcStride, i);
                    break;
                case VMXImageType.YV12:
                    IntPtr srcV = src + (srcStride * height);
                    int strideV = srcStride >> 1;
                    IntPtr srcU = srcV + (strideV * (height >> 1));
                    int strideU = srcStride >> 1;
                    hr = codec.VMX_EncodeYV12(instance, src, srcStride, srcU, strideU, srcV, strideV, i);
                    break;
                case VMXImageType.BGRA:
                    hr = codec.VMX_EncodeBGRA(instance, src, srcStride, i);
                    break;
                case VMXImageType.BGRX:
                    hr = codec.VMX_EncodeBGRA(instance, src, srcStride, i);
                    break;
                default:
                    return 0;
            }            
            if (hr == 0)
            {
                int len = codec.VMX_SaveTo(instance, dst, dst.Length);
                return len;
            }
            return 0;
        }
        public bool DecodePreview(VMXImageType itype, byte[] src, int srcLen, ref byte[] dst, int dstStride)
        {
            int hr = codec.VMX_LoadFrom(instance, src, srcLen);
            if (hr == 0)
            {
                switch (itype)
                {
                    case VMXImageType.BGRA:
                        hr = codec.VMX_DecodePreviewBGRA(instance, dst, dstStride);
                        break;
                    case VMXImageType.BGRX:
                        hr = codec.VMX_DecodePreviewBGRX(instance, dst, dstStride);
                        break;
                    case VMXImageType.UYVY:
                        hr = codec.VMX_DecodePreviewUYVY(instance, dst, dstStride);
                        break;
                    case VMXImageType.UYVA:
                        hr = codec.VMX_DecodePreviewUYVA(instance, dst, dstStride);
                        break;
                    case VMXImageType.YUY2:
                        hr = codec.VMX_DecodePreviewYUY2(instance, dst, dstStride);
                        break;
                    default:
                        return false;
                }
                if (hr == 0) return true;
            }
            return false;
        }
        public bool Decode(VMXImageType itype, byte[] src, int srcLen, ref byte[] dst, int dstStride)
        {
            int hr = codec.VMX_LoadFrom(instance, src, srcLen);
            if (hr == 0)
            {
                switch (itype)
                {
                    case VMXImageType.BGRA:
                        hr = codec.VMX_DecodeBGRA(instance, dst, dstStride);
                        break;
                    case VMXImageType.BGRX:
                        hr = codec.VMX_DecodeBGRX(instance, dst, dstStride);
                        break;
                    case VMXImageType.UYVY:
                        hr = codec.VMX_DecodeUYVY(instance, dst, dstStride);
                        break;
                    case VMXImageType.YUY2:
                        hr = codec.VMX_DecodeYUY2(instance, dst, dstStride);
                        break;
                    case VMXImageType.UYVA:
                        hr = codec.VMX_DecodeUYVA(instance, dst, dstStride);
                        break;
                    case VMXImageType.P216:
                        hr = codec.VMX_DecodeP216(instance, dst, dstStride);
                        break;
                    case VMXImageType.PA16:
                        hr = codec.VMX_DecodePA16(instance, dst, dstStride);
                        break;
                    default:
                        return false;
                }
                if (hr == 0) return true;
            }
            return false;
        }

        public OMTSize GetPreviewSize(bool interlaced)
        {
            OMTSize size = new OMTSize();
            size.Width = width >> 3;
            size.Height = height >> 3;
            if ((size.Width %2) != 0)
            {
                size.Width++;
            }
            if (interlaced)
            {
                if ((size.Height % 2) != 0)
                {
                    size.Height--;
                }
            }
            return size; 
        }
        public int GetEncodedPreviewLength()
        {
            return codec.VMX_GetEncodedPreviewLength(instance);
        }

        public void SetEncodingParameters(int frameMin, int frameMax, int minQuality, int dcShift)
        {
            codec.VMX_SetEncodingParameters(instance, frameMin, frameMax, minQuality, dcShift);
        }

        public void GetEncodingParameters(out int frameMin, out int frameMax, out int minQuality, out int dcShift)
        {
            codec.VMX_GetEncodingParameters(instance, out frameMin, out frameMax, out minQuality, out dcShift);
        }

        protected override void DisposeInternal()
        {
            if (instance != IntPtr.Zero)
            {
                codec.VMX_Destroy(instance);
                instance = IntPtr.Zero;
            }            
            base.DisposeInternal();
        }

        public int Width { get { return width; } }
        public int Height { get { return height; } }
        public int FramesPerSecond { get { return framesPerSecond; } }
        public VMXProfile Profile {  get { return profile; } }
        public VMXColorSpace ColorSpace { get { return colorSpace; } }

       
    }
}
