﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using NAudio.Wave;

namespace Ciribob.DCS.SimpleRadio.Standalone.Client.Audio
{
    //From https://raw.githubusercontent.com/naudio/NAudio/master/NAudio/Wave/SampleProviders/VolumeSampleProvider.cs
    public class VolumeSampleProviderWithPeak : ISampleProvider
    {

        private readonly ISampleProvider source;
        private readonly SamplePeak _samplePeak;
        private float volume;

        public delegate void SamplePeak(float peak);

        /// <summary>
        /// Initializes a new instance of VolumeSampleProvider
        /// </summary>
        /// <param name="source">Source Sample Provider</param>
        public VolumeSampleProviderWithPeak(ISampleProvider source, SamplePeak samplePeak)
        {
            this.source = source;
            _samplePeak = samplePeak;
            this.volume = 1.0f;
        }

        /// <summary>
        /// WaveFormat
        /// </summary>
        public WaveFormat WaveFormat
        {
            get { return source.WaveFormat; }
        }

        /// <summary>
        /// Reads samples from this sample provider
        /// </summary>
        /// <param name="buffer">Sample buffer</param>
        /// <param name="offset">Offset into sample buffer</param>
        /// <param name="sampleCount">Number of samples desired</param>
        /// <returns>Number of samples read</returns>
        public int Read(float[] buffer, int offset, int sampleCount)
        {
            int samplesRead = source.Read(buffer, offset, sampleCount);

            float max = 0;
            for (int n = 0; n < sampleCount; n++)
            {
                var sample = buffer[offset + n];
                sample *= volume;

                buffer[offset + n] = sample;

                if (sample > max)
                {
                    max = sample;
                }
            }

            _samplePeak(max);

            return samplesRead;
        }

        /// <summary>
        /// Allows adjusting the volume, 1.0f = full volume
        /// </summary>
        public float Volume
        {
            get { return volume; }
            set { volume = value; }
        }
    }
}
