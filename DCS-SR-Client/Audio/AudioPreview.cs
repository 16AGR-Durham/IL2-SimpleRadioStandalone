﻿using System;
using System.Collections.Generic;
using Ciribob.DCS.SimpleRadio.Standalone.Client.DSP;
using FragLabs.Audio.Codecs;
using FragLabs.Audio.Codecs.Opus;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using NLog;

namespace Ciribob.DCS.SimpleRadio.Standalone.Client.Audio
{
    internal class AudioPreview
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
        private BufferedWaveProvider _playBuffer;
        private WaveIn _waveIn;
        private WaveOut _waveOut;

        private VolumeSampleProviderWithPeak _volumeSampleProvider;
        private BufferedWaveProvider _buffBufferedWaveProvider;

        public float MicBoost { get; set; } = 1.0f;

        private float _speakerBoost = 1.0f;
        private OpusEncoder _encoder;
        private OpusDecoder _decoder;

        private readonly Queue<byte> _micInputQueue = new Queue<byte>(AudioManager.SEGMENT_FRAMES * 3);

        public float SpeakerBoost
        {
            get { return _speakerBoost; }
            set
            {
                _speakerBoost = value;
                if (_volumeSampleProvider != null)
                {
                    _volumeSampleProvider.Volume = value;
                }
            }
        }

        public short MicMax { get; set; }
        public float SpeakerMax { get; set; }

        public void StartPreview(int mic, int speakers)
        {
            try
            {
                _waveOut = new WaveOut
                {
                    DesiredLatency = 160, // half to get tick rate - so 40ms
                    DeviceNumber = speakers
                };

                _buffBufferedWaveProvider = new BufferedWaveProvider(new WaveFormat(AudioManager.INPUT_SAMPLE_RATE, 16, 1));
                _buffBufferedWaveProvider.ReadFully = true;

                RadioFilter filter = new RadioFilter(_buffBufferedWaveProvider.ToSampleProvider());

                //add final volume boost to all mixed audio
                _volumeSampleProvider = new VolumeSampleProviderWithPeak(filter, (peak => SpeakerMax = peak));
                _volumeSampleProvider.Volume = SpeakerBoost;

                //resample client audio to 44100
                var resampler = new WdlResamplingSampleProvider(_volumeSampleProvider, AudioManager.OUTPUT_SAMPLE_RATE);
                //resample and output at 44100

                _waveOut.Init(resampler);

                _waveOut.Play();

                //opus
                _encoder = OpusEncoder.Create(AudioManager.INPUT_SAMPLE_RATE, 1, Application.Voip);
                _encoder.ForwardErrorCorrection = false;
                _decoder = OpusDecoder.Create(AudioManager.INPUT_SAMPLE_RATE, 1);
                _decoder.ForwardErrorCorrection = false;

                _waveIn = new WaveIn(WaveCallbackInfo.FunctionCallback())
                {
                    BufferMilliseconds = 100,
                    DeviceNumber = mic
                };

                _waveIn.NumberOfBuffers = 1;
                _waveIn.DataAvailable += _waveIn_DataAvailable;
                _waveIn.WaveFormat = new WaveFormat(AudioManager.INPUT_SAMPLE_RATE, 16, 1);

                _waveIn.StartRecording();

            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Error starting audio Quitting! " + ex.Message);

                Environment.Exit(1);
            }
        }

        private void _waveIn_DataAvailable(object sender, WaveInEventArgs e)
        {

            //fill sound buffer

            byte[] soundBuffer = null;
            if ((e.BytesRecorded == AudioManager.SEGMENT_FRAMES) && (_micInputQueue.Count == 0))
            {
                //perfect!
                soundBuffer = new byte[e.BytesRecorded];
                Buffer.BlockCopy(e.Buffer, 0, soundBuffer, 0, e.BytesRecorded);
            }
            else
            {
                for (var i = 0; i < e.BytesRecorded; i++)
                {
                    _micInputQueue.Enqueue(e.Buffer[i]);
                }
            }

            //read out the queue
            while ((soundBuffer != null) || (_micInputQueue.Count >= AudioManager.SEGMENT_FRAMES))
            {
                //null sound buffer so read from the queue
                if (soundBuffer == null)
                {
                    soundBuffer = new byte[AudioManager.SEGMENT_FRAMES];

                    for (var i = 0; i < AudioManager.SEGMENT_FRAMES; i++)
                    {
                        soundBuffer[i] = _micInputQueue.Dequeue();
                    }
                }

                short max = 0;
                for (var n = 0; n < soundBuffer.Length; n += 2)
                {
                    var sample = (short)((soundBuffer[n + 1] << 8) | soundBuffer[n + 0]);

                    // n.b. no clipping test going on here // FROM NAUDIO SOURCE !
                    sample = (short)(sample * MicBoost);

                    //determine peak
                    if (sample > max)
                        max = sample;

                    //convert back
                    soundBuffer[n] = (byte)(sample & 0xFF);
                    soundBuffer[n + 1] = (byte)(sample >> 8);
                }

                MicMax = max;

                try
                {
                    //encode as opus bytes
                    int len;
                    var buff = _encoder.Encode(soundBuffer, soundBuffer.Length, out len);

                    if ((buff != null) && (len > 0))
                    {
                        //create copy with small buffer
                        var encoded = new byte[len];

                        Buffer.BlockCopy(buff, 0, encoded, 0, len);

                        var decodedLength = 0;
                        //now decode
                        var decodedBytes = _decoder.Decode(encoded, len, out decodedLength);

                        _buffBufferedWaveProvider.AddSamples(decodedBytes, 0, decodedLength);


                    }
                    else
                    {
                        Logger.Error($"Invalid Bytes for Encoding - {e.BytesRecorded} should be {AudioManager.SEGMENT_FRAMES} ");
                    }
                }
                catch (Exception ex)
                {
                    Logger.Error(ex, "Error encoding Opus! " + ex.Message);
                }

                soundBuffer = null;
            }
        }

        public void StopEncoding()
        {

            if (_waveIn != null)
            {
                _waveIn.StopRecording();
                _waveIn.Dispose();
                _waveIn = null;
            }

            if (_waveOut != null)
            {
                _waveOut.Stop();
                _waveOut.Dispose();
                _waveOut = null;
            }

            if (_playBuffer != null)
            {
                _playBuffer.ClearBuffer();
                _playBuffer = null;
            }


            if (_encoder != null)
            {
                _encoder.Dispose();
                _encoder = null;
            }

            if (_decoder != null)
            {
                _decoder.Dispose();
                _decoder = null;
            }

            SpeakerMax = 0;
            MicMax = 0;

            if (_playBuffer != null)
            {
                _playBuffer.ClearBuffer();
                _playBuffer = null;
            }
        }
    }
}