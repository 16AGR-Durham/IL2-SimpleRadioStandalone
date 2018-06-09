﻿namespace Ciribob.DCS.SimpleRadio.Standalone.Common
{
    public class RadioReceivingPriority
    {
        public double Frequency;
        public byte Encryption;
        public short Modulation;
        public float LineOfSightLoss;
        public double ReceivingPowerLossPercent;
        public bool CanReceive;
        public bool Decryptable;

        public RadioReceivingState ReceivingState;
        public RadioInformation ReceivingRadio;
    }
}