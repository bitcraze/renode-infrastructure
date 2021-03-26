//
// Copyright (c) 2021 Bitcraze
// Copyright (c) 2010-2020 Antmicro
//
// This file is licensed under the MIT License.
// Full license text is available in 'licenses/MIT.txt'.
//
using System;
using System.Linq;
using System.Collections.Generic;
using Antmicro.Renode.Peripherals.Sensor;
using Antmicro.Renode.Peripherals.I2C;
using Antmicro.Renode.Logging;
using Antmicro.Renode.Core;
using Antmicro.Renode.Core.Structure.Registers;
using Antmicro.Renode.Utilities;

namespace Antmicro.Renode.Peripherals.Sensors
{
    public class BMP388 : II2CPeripheral, IProvidesRegisterCollection<ByteRegisterCollection>, ISensor
    {
        public BMP388()
        {
            fifoP = new SensorSamplesFifo<ScalarSample>();
            fifoT = new SensorSamplesFifo<ScalarSample>();
            RegistersCollection = new ByteRegisterCollection(this);
            Int1 = new GPIO();
            DefineRegisters();
        }

        public void Reset()
        {
            RegistersCollection.Reset();
            registerAddress = 0;
            Int1.Unset();
            this.Log(LogLevel.Noisy, "Reset registers");
        }

        public void Write(byte[] data)
        {
            if(data.Length == 0)
            {
                this.Log(LogLevel.Warning, "Unexpected write with no data");
                return;
            }

            this.Log(LogLevel.Noisy, "Write with {0} bytes of data: {1}", data.Length, Misc.PrettyPrintCollectionHex(data));
            registerAddress = (Registers)data[0];

            // length=1 preparing to read
            // length=3 one byte of data
            // (n*2)+2 burst write with n bytes

            // Skip the first byte as it contains register address
            // Must skip final byte, problem with I2C

            if(data.Length == 1)
            {
                this.Log(LogLevel.Noisy, "Preparing to read register {0} (0x{0:X})", registerAddress);
            }
            else if(data.Length == 3)
            {
                RegistersCollection.Write((byte)registerAddress, data[1]);
                this.Log(LogLevel.Noisy, "Writing one byte 0x{0:X} to register {1} (0x{1:X})", data[1], registerAddress);
            }
            else
            {
                // Burst write causes one extra trash byte to be transmitted in addition
                // to the extra I2C byte.
                this.Log(LogLevel.Noisy, "Burst write mode!");
                for(var i = 0; 2*i < data.Length-2; i++)
                {
                    RegistersCollection.Write(data[2*i], data[2*i+1]);
                    this.Log(LogLevel.Noisy, "Writing 0x{0:X} to register {1} (0x{1:X})", data[2*i+1], (Registers)data[2*i]);
                }
            }
        }

        public byte ReadRegister(byte offset)
        {
            return RegistersCollection.Read(offset);
        }

        public byte[] Read(int count)
        {
            if(registerAddress==Registers.Data0)
            {
                fifoP.TryDequeueNewSample();
                fifoT.TryDequeueNewSample();
            }

            var result = new byte[registerAddress==Registers.Data0?6 : registerAddress==Registers.OSR?4 : registerAddress==Registers.Calib0?21 : 1 ];
            for(var i = 0; i < result.Length; i++)
            {
                result[i] = RegistersCollection.Read((byte)registerAddress + i);
                this.Log(LogLevel.Noisy, "Read value 0x{0:X} from register {1} (0x{1:X})", result[i], (Registers)registerAddress + i);
            }
            return result;
        }

        public void FinishTransmission()
        {
        }

        public ByteRegisterCollection RegistersCollection { get; }
        public GPIO Int1 { get; }

        public void TriggerDataInterrupt()
        {
           // TODO: TriggerDataInterrupt
        }

        public void FeedPTSample(decimal pressure, decimal temperature, int repeat = 1)
        {

            var sampleP = new ScalarSample(pressure);
            var sampleT = new ScalarSample(temperature);
            for(var i = 0; i < repeat; i++)
            {
                fifoP.FeedSample(sampleP);
                fifoT.FeedSample(sampleT);
            }
        }

        public void FeedPTSample(string pathP, string pathT)
        {
            fifoP.FeedSamplesFromFile(pathP);
            fifoT.FeedSamplesFromFile(pathT);
        }

        private void DefineRegisters()
        {
            Registers.ChipId.Define(this, 0x50); //RO
            Registers.ErrReg.Define(this, 0x00); //RO
            Registers.Status.Define(this, 0x10); //RO HACK wrong reset value, command decoder always ready in simulation
            Registers.Data0.Define(this, 0x00)
                .WithValueField(0, 8, FieldMode.Read, name: "Press_[7:0]", valueProviderCallback: _ => PtoByte(fifoP.Sample.Value, 0)); //RO
            Registers.Data1.Define(this, 0x00)
                .WithValueField(0, 8, FieldMode.Read, name: "Press_[15:8]", valueProviderCallback: _ => PtoByte(fifoP.Sample.Value, 8)); //RO
            Registers.Data2.Define(this, 0x80)
                .WithValueField(0, 8, FieldMode.Read, name: "Press_[23:16]", valueProviderCallback: _ => PtoByte(fifoP.Sample.Value, 16)); //RO
            Registers.Data3.Define(this, 0x00)
                .WithValueField(0, 8, FieldMode.Read, name: "Temp_[7:0]", valueProviderCallback: _ => TtoByte(fifoT.Sample.Value, 0)); //RO
            Registers.Data4.Define(this, 0x00)
                .WithValueField(0, 8, FieldMode.Read, name: "Temp_[15:8]", valueProviderCallback: _ => TtoByte(fifoT.Sample.Value, 8)); //RO
            Registers.Data5.Define(this, 0x80)
                .WithValueField(0, 8, FieldMode.Read, name: "Temp_[23:16]", valueProviderCallback: _ => TtoByte(fifoT.Sample.Value, 16)); //RO

            Registers.IntCtrl.Define(this, 0x02);
            Registers.IfConf.Define(this, 0x00);
            Registers.PwrCtrl.Define(this, 0x00);
            Registers.OSR.Define(this, 0x02) //RW
                .WithValueField(0, 3, name: "osr_p")
                .WithValueField(3, 3, name: "osr_t")
                .WithReservedBits(6, 2);
            Registers.ODR.Define(this, 0x00) //RW
                .WithValueField(0, 5, name: "odr_sel")
                .WithReservedBits(5, 3);
            Registers.Config.Define(this, 0x00) //RW
                .WithReservedBits(0, 1)
                .WithValueField(1, 3, name: "iir_filter")
                .WithReservedBits(4, 4);

            Registers.Cmd.Define(this, 0x00) //WO
                .WithWriteCallback((_, val) =>
                {
                    if(val == resetCommand)
                    {
                        Reset();
                    }
                });

            // Read from one real sensor
            Registers.Calib0.Define(this, 0xC5);
            Registers.Calib1.Define(this, 0x6A);
            Registers.Calib2.Define(this, 0xDD);
            Registers.Calib3.Define(this, 0x48);
            Registers.Calib4.Define(this, 0xF6);
            Registers.Calib5.Define(this, 0xE8);
            Registers.Calib6.Define(this, 0x01);
            Registers.Calib7.Define(this, 0x25);
            Registers.Calib8.Define(this, 0xF7);
            Registers.Calib9.Define(this, 0x23);
            Registers.Calib10.Define(this, 0x00);
            Registers.Calib11.Define(this, 0x21);
            Registers.Calib12.Define(this, 0x60);
            Registers.Calib13.Define(this, 0x9E);
            Registers.Calib14.Define(this, 0x75);
            Registers.Calib15.Define(this, 0xF3);
            Registers.Calib16.Define(this, 0xF6);
            Registers.Calib17.Define(this, 0x78);
            Registers.Calib18.Define(this, 0x40);
            Registers.Calib19.Define(this, 0x13);
            Registers.Calib20.Define(this, 0xC4);
        }

        private Registers registerAddress;
        private readonly SensorSamplesFifo<ScalarSample> fifoP;
        private readonly SensorSamplesFifo<ScalarSample> fifoT;

        private const byte resetCommand = 0xB6;

        private byte PtoByte(decimal rawData, byte shift)
        {
            //FIXME add conversion
            int converted = (int)(rawData > 0xFFFFFF ? 0xFFFFFF : rawData);
            return (byte)(converted >> shift);
        }

        private byte TtoByte(decimal rawData, byte shift)
        {
            //FIXME add conversion
            int converted = (int)(rawData > 0xFFFFFF ? 0xFFFFFF : rawData);
            return (byte)(converted >> shift);
        }

        private enum Registers
        {
            ChipId = 0x00, // Read-Only
            // 0x01 reserved
            ErrReg = 0x02, // Read-Only
            Status = 0x03, // Read-Only
            Data0 = 0x04, // Read-Only
            Data1 = 0x05, // Read-Only
            Data2 = 0x06, // Read-Only
            Data3 = 0x07, // Read-Only
            Data4 = 0x08, // Read-Only
            Data5 = 0x09, // Read-Only
            // 0x0A - 0x0B reserved
            Sensortime0 = 0x0C, // Read-Only
            Sensortime1 = 0x0D, // Read-Only
            Sensortime2 = 0x0E, // Read-Only
            Event = 0x10, // Read-Only
            IntStatus = 0x11,  // Read-Only
            FIFOLength0 = 0x12, // Read-Only
            FIFOLength1 = 0x13, // Read-Only
            FIFOData = 0x14, // Read-Only
            FIFOWtm0 = 0x15, // Read-Write
            FIFOWtm1 = 0x16, // Read-Write
            FIFOConfig1 = 0x17, // Read-Write
            FIFOConfig2 = 0x18, // Read-Write
            IntCtrl = 0x19, // Read-Write
            IfConf = 0x1A, // Read-Write
            PwrCtrl = 0x1B, // Read-Write
            OSR = 0x1C, // Read-Write
            ODR = 0x1D, // Read-Write
            // 0x1E reserved
            Config = 0x1F, // Read-Write
            // 0x20 - 0x7D reserved
            Cmd = 0x7E, // Read-Write

            Calib0 = 0x31,
            Calib1 = 0x32,
            Calib2 = 0x33,
            Calib3 = 0x34,
            Calib4 = 0x35,
            Calib5 = 0x36,
            Calib6 = 0x37,
            Calib7 = 0x38,
            Calib8 = 0x39,
            Calib9 = 0x3A,
            Calib10 = 0x3B,
            Calib11 = 0x3C,
            Calib12 = 0x3D,
            Calib13 = 0x3E,
            Calib14 = 0x3F,
            Calib15 = 0x40,
            Calib16 = 0x41,
            Calib17 = 0x42,
            Calib18 = 0x43,
            Calib19 = 0x44,
            Calib20 = 0x45
        }
    }
}
