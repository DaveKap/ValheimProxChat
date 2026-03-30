using System;

namespace ValheimProxChat.Audio
{
    /// <summary>
    /// Simple mu-law audio compression for voice data.
    /// Compresses 16-bit PCM samples to 8-bit mu-law, halving bandwidth
    /// with minimal quality loss for voice frequencies.
    /// </summary>
    public static class AudioCompression
    {
        private const int MuLawBias = 0x84;
        private const int MuLawClip = 32635;

        private static readonly int[] ExpLut =
        {
            0, 0, 1, 1, 2, 2, 2, 2, 3, 3, 3, 3, 3, 3, 3, 3,
            4, 4, 4, 4, 4, 4, 4, 4, 4, 4, 4, 4, 4, 4, 4, 4,
            5, 5, 5, 5, 5, 5, 5, 5, 5, 5, 5, 5, 5, 5, 5, 5,
            5, 5, 5, 5, 5, 5, 5, 5, 5, 5, 5, 5, 5, 5, 5, 5,
            6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6,
            6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6,
            6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6,
            6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6,
            7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7,
            7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7,
            7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7,
            7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7,
            7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7,
            7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7,
            7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7,
            7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7
        };

        /// <summary>
        /// Encode a single 16-bit PCM sample to 8-bit mu-law.
        /// </summary>
        public static byte EncodeSample(short sample)
        {
            int sign = (sample >> 8) & 0x80;
            if (sign != 0)
                sample = (short)-sample;
            if (sample > MuLawClip)
                sample = MuLawClip;

            sample = (short)(sample + MuLawBias);
            int exponent = ExpLut[(sample >> 7) & 0xFF];
            int mantissa = (sample >> (exponent + 3)) & 0x0F;
            int muLawByte = ~(sign | (exponent << 4) | mantissa);

            return (byte)(muLawByte & 0xFF);
        }

        /// <summary>
        /// Decode a single 8-bit mu-law sample to 16-bit PCM.
        /// </summary>
        public static short DecodeSample(byte muLaw)
        {
            muLaw = (byte)~muLaw;
            int sign = muLaw & 0x80;
            int exponent = (muLaw >> 4) & 0x07;
            int mantissa = muLaw & 0x0F;
            int sample = ((mantissa << 3) + MuLawBias) << exponent;
            sample -= MuLawBias;
            if (sign != 0)
                sample = -sample;
            return (short)sample;
        }

        /// <summary>
        /// Compress float PCM samples [-1.0, 1.0] to mu-law encoded byte array.
        /// </summary>
        public static byte[] Compress(float[] samples, int offset, int count)
        {
            byte[] compressed = new byte[count];
            for (int i = 0; i < count; i++)
            {
                float s = samples[offset + i];
                // Clamp to [-1, 1]
                if (s > 1f) s = 1f;
                else if (s < -1f) s = -1f;
                short pcm16 = (short)(s * 32767f);
                compressed[i] = EncodeSample(pcm16);
            }
            return compressed;
        }

        /// <summary>
        /// Decompress mu-law encoded byte array to float PCM samples [-1.0, 1.0].
        /// </summary>
        public static float[] Decompress(byte[] data, int offset, int count)
        {
            float[] samples = new float[count];
            for (int i = 0; i < count; i++)
            {
                short pcm16 = DecodeSample(data[offset + i]);
                samples[i] = pcm16 / 32768f;
            }
            return samples;
        }
    }
}
