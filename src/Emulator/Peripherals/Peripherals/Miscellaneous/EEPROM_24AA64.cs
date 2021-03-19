//
// Copyright (c) 2010-2019 Antmicro
//
// This file is licensed under the MIT License.
// Full license text is available in 'licenses/MIT.txt'.
//
using System;
using System.Collections.Generic;
using System.Linq;
using Antmicro.Renode.Exceptions;
using Antmicro.Renode.Logging;
using Antmicro.Renode.Peripherals.I2C;
using Antmicro.Renode.Utilities;

namespace Antmicro.Renode.Peripherals.Miscellaneous
{
    public class EEPROM_24AA64 : II2CPeripheral
    {
        public EEPROM_24AA64()
        {
            Array.Copy(configdata, storage, configdata.Length);
        }

        public byte[] Read(int count = 1)
        {
            // Currently no way to know how many bytes are read and thus
            // Sequential Read cannot properly increase the address counter
            // DO NOT use current address read after sequential read!
            this.Log(LogLevel.Noisy, "Reading 0x{0:X} bytes from EEPROM at address 0x{1:X}", count, ((ushort)highAddress<<8)+lowAddress);
            byte[] result = new byte[storage.Length];

            var ap = ((ushort)highAddress<<8)+lowAddress;
            Array.Copy(storage, ap, result, 0, storage.Length - ap);
            Array.Copy(storage, 0, result, storage.Length - ap, ap);

            lowAddress++;
            if(lowAddress == 0)
            {
                highAddress++;
                highAddress &= 0x1F;
            }
            return result;
        }

        public void Write(byte[] packet)
        {
            // Implemented: Byte and Page Write
            if(packet.Length < 2)
            {
                this.Log(LogLevel.Error, "Tried to write less than two bytes, i.e. missing address bytes.");
                return;
            }
            if(packet.Length > 34)
            {
                this.Log(LogLevel.Warning, "Page overflow. Trying to write {0} bytes of data and new data will be overwritten!", packet.Length-2);
            }
            if((packet[0] & ~0x1F) != 0)
            {
                this.Log(LogLevel.Warning, "Unused bits: 0x{0:X} for high address ignored!", packet[0] & ~0x1F);
            }
            highAddress = (byte)(packet[0] & 0x1F);
            lowAddress = packet[1];
            byte inPageAddress = (byte)(lowAddress & 0x1F);
            ushort pageAddress = (ushort)(((ushort)highAddress << 8) + (lowAddress & 0xE0));
            this.Log(LogLevel.Noisy, "inPageAddress: 0x{0:X}, pageAddress: 0x{1:X}, packet length: {2}",inPageAddress, pageAddress, packet.Length);
            //FIXME The I2C seems to send one packet too much of transmited data, workaround by ignoring the final byte (i.e. -3 instead of -2)?
            for(int i = 0; i < packet.Length - 3; ++i)
            {
                storage[pageAddress + ((inPageAddress+i)%0x20)] = packet[i+2]; // Page overflow
                this.Log(LogLevel.Noisy, "Trying to write data byte {0} (value: 0x{1:X})", i, packet[i+2]);
            }
            //TODO Handle the write delay!
        }

        public void FinishTransmission()
        {
           this.Log(LogLevel.Noisy, "FinishTransmission in EEPROM!");
        }

        public void Reset()
        {
        }

        public void testWrite(int option)
        {
            switch(option)
            {
            case 0:
                Write(new byte[]{0x00});
                break;
            case 1:
                Write(new Byte[]{0x01,0x01});
                break;
            case 2:
                Write(new Byte[]{0x00,0x00,0xBC,0xCF});
                break;
            case 3:
                Write(new byte[40]);
                break;
            case 4:
                Write(new Byte[]{0x30, 0x78, 0x42, 0x43, 0x01, 0x50, 0x02, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0xE7, 0xE7, 0xE7, 0xE7, 0xE7, 0x04, 0x30, 0x78, 0x42, 0x43, 0x01, 0x50, 0x02, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0xE7, 0xE7, 0xE7, 0xE7, 0xE7, 0x04});
                break;
            case 5:
                Write(new Byte[]{0x36,0x10,0xBC,0xCF});
                break;
            }
            return;
        }

        private byte[] configdata = {0x30, 0x78, 0x42, 0x43, 0x01, 0x50, 0x02, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0xE7, 0xE7, 0xE7, 0xE7, 0xE7, 0x03};
        private byte[] storage = new byte[8192];
        private byte highAddress;
        private byte lowAddress;
    }
}
