using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Timers;

namespace Chippy
{
    public class Chip8
    {
        /// <summary>
        /// Default clock frequency for the CPU (Hz)
        /// </summary>
        private static readonly uint CLOCK_CPU_FREQ = 860;

        /// <summary>
        /// Clock frequency for the timers (Hz)
        /// </summary>
        private static readonly uint CLOCK_TIMERS_FREQ = 60;

        /// <summary>
        /// Memory dump with the fonts
        /// </summary>
        private static readonly byte[] FONTS = { 
            0xF0, 0x90, 0x90, 0x90, 0xF0, //0
            0x20, 0x60, 0x20, 0x20, 0x70, //1
            0xF0, 0x10, 0xF0, 0x80, 0xF0, //2
            0xF0, 0x10, 0xF0, 0x10, 0xF0, //3
            0x90, 0x90, 0xF0, 0x10, 0x10, //4
            0xF0, 0x80, 0xF0, 0x10, 0xF0, //5
            0xF0, 0x80, 0xF0, 0x90, 0xF0, //6
            0xF0, 0x10, 0x20, 0x40, 0x40, //7
            0xF0, 0x90, 0xF0, 0x90, 0xF0, //8
            0xF0, 0x90, 0xF0, 0x10, 0xF0, //9
            0xF0, 0x90, 0xF0, 0x90, 0x90, //A
            0xE0, 0x90, 0xE0, 0x90, 0xE0, //B
            0xF0, 0x80, 0x80, 0x80, 0xF0, //C
            0xE0, 0x90, 0x90, 0x90, 0xE0, //D
            0xF0, 0x80, 0xF0, 0x80, 0xF0, //E
            0xF0, 0x80, 0xF0, 0x80, 0x80  //F
        };

        /// <summary>
        /// Last OP code. (16 bits)
        /// </summary>
        private UInt16 opcode = 0;

        /// <summary>
        /// Memory (8 bits x 4096)
        /// </summary>
        private byte[] memory = new byte[4096];

        /// <summary>
        /// Data registers (8 bits x 16)
        /// </summary>
        private byte[] v = new byte[16];

        /// <summary>
        /// Address register (16 bits)
        /// </summary>
        private UInt16 i = 0;

        /// <summary>
        /// Program counter (16 bits)
        /// </summary>
        private UInt16 pc = 0x200;

        /// <summary>
        /// Stack (16 levels)
        /// </summary>
        private Stack<UInt16> stack = new Stack<ushort>(16);
    
        /// <summary>
        /// Buffer of video (8bits * 64px * 32px)
        /// </summary>
        private byte[] video = new byte[64 * 32];

        /// <summary>
        /// Buffer of pressed keys
        /// </summary>
        private byte[] key = new byte[16];

        /// <summary>
        /// Lock for thread safe usage.
        /// </summary>
        private readonly object lockChip = new object();

        /// <summary>
        /// Random number generator.
        /// </summary>
        private Random rand = new Random();
        
        /// <summary>
        /// Timer for delays
        /// </summary>
        private byte timer_delay = 0x00;

        /// <summary>
        /// Timer for sound effects
        /// </summary>
        private byte timer_sound = 0x00;

        /// <summary>
        /// Timer to simulate the clock of the timers
        /// </summary>
        private System.Timers.Timer clock_timers;

        /// <summary>
        /// Timer to simulate the clock of the CPU
        /// </summary>
        private System.Timers.Timer clock_cpu;

        /// <summary>
        /// HZ with the max frequency of the clock
        /// </summary>
        private uint clock_cpu_hz = Chip8.CLOCK_CPU_FREQ;

        public event ScreenChangedEventHandler ScreenChanged;

        public void Start()
        {
            this.clock_timers = new System.Timers.Timer(1000 / CLOCK_TIMERS_FREQ);
            this.clock_timers.Elapsed += new ElapsedEventHandler(this.TimerClockCPUHandle);

            this.clock_cpu = new System.Timers.Timer(1000 / this.clock_cpu_hz);
            this.clock_cpu.Elapsed += new ElapsedEventHandler(this.TimerClockCPUHandle);

            this.clock_timers.Enabled = true;
            this.clock_cpu.Enabled = true;
        }

        public void Stop()
        {
            if (this.clock_timers != null)
            {
                this.clock_timers.Enabled = false;
                this.clock_timers.Dispose();
                this.clock_timers = null;
            }

            if (this.clock_cpu != null)
            {
                this.clock_cpu.Enabled = false;
                this.clock_cpu.Dispose();
                this.clock_cpu = null;
            }
        }
        
        /// <summary>
        /// Reset the chip
        /// </summary>
        public void Reset() {
            // Initializing internal records
            this.opcode = 0;
            this.pc     = 0x200;
            this.i      = 0;
            this.v.Initialize();
            this.stack = new Stack<ushort>();

            // Initializing timers
            this.timer_delay = 0;
            this.timer_sound = 0;
            this.clock_cpu_hz = (this.clock_cpu_hz > 0) ? this.clock_cpu_hz : Chip8.CLOCK_CPU_FREQ;

            byte[] newmemory = new byte[4096];
            Array.Copy(this.memory, 512, newmemory, 512, this.memory.Length - 512);

            this.video.Initialize();
            this.key.Initialize();
        }

        private void TimerClockCPUHandle(object sender, ElapsedEventArgs e)
        {
            step();
        }

        private void TimerClockTimerHandle(object sender, ElapsedEventArgs e)
        {
            if (this.timer_delay == 0x01) { /* Event */ }
            if (this.timer_sound == 0x01) { /* Event */ }

            if (this.timer_delay > 0) { this.timer_delay--; }
            if (this.timer_sound > 0) { this.timer_sound--; }
        }

        /// <summary>
        /// Perform an CPU operation
        /// </summary>
        private Boolean step()
        {
            lock (lockChip)
            {
                this.opcode = (UInt16)(this.memory[this.pc] << 8 | this.memory[this.pc + 1]);

                // First byte mask
                ushort codefmask = (ushort)(this.opcode & 0xF000);

                // 0x0...
                if (codefmask == 0x0000)
                {
                    switch (this.opcode)
                    {
                        // 0x00E0 -> Clear video buffer
                        case 0x00E0:
                            this.video.Initialize();
                            if (this.ScreenChanged != null) ScreenChanged(this, this.video);
                            this.pc += 2;
                            break;

                        // 0x00EE -> Returns from subrutine
                        case 0x00EE:
                            this.pc = (UInt16)(this.stack.Pop() + 2);
                            break;

                        default:
                            Console.WriteLine("OPCode desconocido: " + this.opcode.ToString("X4"));
                            return false;
                    }
                }

                // 0x1... -> Jumps to address ...
                else if (codefmask == 0x1000)
                {
                    this.pc = (UInt16)(this.opcode & 0x0FFF);
                }

                // 0x2... -> Call subroutine at ... 
                else if (codefmask == 0x2000)
                {
                    this.stack.Push(this.pc); // Save current program counter
                    this.pc = (UInt16)(this.opcode & 0x0FFF);
                }

                // 0x3X.. -> Skip next instruction if .. equal to register VX
                else if (codefmask == 0x3000)
                {
                    byte vindex = (byte)((this.opcode & 0x0F00) >> 8);
                    this.pc += (UInt16)((this.v[vindex] == (byte)(this.opcode & 0x00FF)) ? 4 : 2);
                }

                // 0x4X.. -> Skip next instruction if .. diferent to register VX
                else if (codefmask == 0x4000)
                {
                    byte vindex = (byte)((this.opcode & 0x0F00) >> 8);
                    this.pc += (UInt16)((this.v[vindex] != (byte)(this.opcode & 0x00FF)) ? 4 : 2);
                }

                // 0x5XY0 -> Skip next instruction register VX equal to VY
                else if (codefmask == 0x5000)
                {
                    byte vx = (byte)((this.opcode & 0x0F00) >> 8);
                    byte vy = (byte)((this.opcode & 0x00F0) >> 4);
                    this.pc += (UInt16)((this.v[vx] == this.v[vy]) ? 4 : 2);
                }

                // 0x6X.. -> Set the register VX to ..
                else if (codefmask == 0x6000)
                {
                    byte vindex = (byte)((this.opcode & 0x0F00) >> 8);
                    this.v[vindex] = (byte)(this.opcode & 0x00FF);
                    this.pc += 2;
                }

                // 0x7X.. -> Add .. to the register VX
                else if (codefmask == 0x7000)
                {
                    byte vindex = (byte)((this.opcode & 0x0F00) >> 8);
                    this.v[vindex] += (byte)(this.opcode & 0x00FF);
                    this.pc += 2;
                }

                // 0x8X..
                else if (codefmask == 0x8000)
                {
                    ushort codelmask = (ushort)(this.opcode & 0x000F);
                    byte vx = (byte)((this.opcode & 0x0F00) >> 8);
                    byte vy = (byte)((this.opcode & 0x00F0) >> 4);

                    // 0x8XY0 -> Sets VX to the value of VY
                    if (codelmask == 0x0000)
                    {
                        this.v[vx] = this.v[vy];
                        this.pc += 2;
                    }

                    // 0x8XY1 -> Sets VX to "VX OR VY"
                    else if (codelmask == 0x0001)
                    {
                        this.v[vx] |= this.v[vy];
                        this.pc += 2;
                    }

                    // 0x8XY2 -> Sets VX to "VX AND VY"
                    else if (codelmask == 0x0002)
                    {
                        this.v[vx] &= this.v[vy];
                        this.pc += 2;
                    }

                    // 0x8XY3 -> Sets VX to "VX XOR VY"
                    else if (codelmask == 0x0003)
                    {
                        this.v[vx] ^= this.v[vy];
                        this.pc += 2;
                    }

                    // 0x8XY4 -> Set VX = VX + VY (VF = 0x00 if VX + VY <= 0xFF, VF = 0x01 if VX + VY > FF)
                    else if (codelmask == 0x0004)
                    {
                        this.v[0xf] = (byte)((this.v[vy] > (0xff - this.v[vx])) ? 1 : 0);
                        this.v[vx] += this.v[vy];
                        this.pc += 2;
                    }

                    // 0x8XY5 -> Set VX = VX - VY (VF = 0x00 if VX < VY, VF = 0x01 if VX > VY)
                    else if (codelmask == 0x0005)
                    {
                        this.v[0xf] = (byte)((this.v[vy] > (0xff - this.v[vx])) ? 0 : 1);
                        this.v[vx] -= this.v[vy];
                        this.pc += 2;
                    }

                    #region New set of instructions
                    // 0x8XY6 -> Shifts VX right by one. VF is set to the value of the least significant bit of VX before the shift
                    else if (codelmask == 0x0006)
                    {
                        this.v[0xf] = (byte)(this.v[vx] & 0x1);
                        this.v[vx] >>= 1;
                        this.pc += 2;
                    }

                    // 0x8XY7 -> Sets VX to VY minus VX. VF is set to 0 when there's a borrow, and 1 when there isn't
                    else if (codelmask == 0x0007)
                    {
                        this.v[0xf] = (byte)((this.v[vx] > this.v[vx]) ? 0 : 1);
                        this.v[vx] = (byte)(this.v[vy] - this.v[vx]);
                        this.pc += 2;
                    }

                    // 0x8XYE -> Shifts VX left by one. VF is set to the value of the most significant bit of VX before the shift
                    else if (codelmask == 0x000E)
                    {
                        this.v[0xf] = (byte)(this.v[vx] >> 7);
                        this.v[vx] <<= 1;
                        this.pc += 2;
                    }
                    #endregion

                    else
                    {
                        Console.WriteLine("OPCode desconocido: " + this.opcode.ToString("X4"));
                        return false;
                    }
                }

                // 0x9XY0 -> Skip next instruction register VX equal to VY
                else if (codefmask == 0x9000)
                {
                    byte vx = (byte)((this.opcode & 0x0F00) >> 8);
                    byte vy = (byte)((this.opcode & 0x00F0) >> 4);
                    this.pc += (UInt16)((this.v[vx] != this.v[vy]) ? 4 : 2);
                }

                // 0xA... -> Store ... in I registry
                else if (codefmask == 0xA000)
                {
                    this.i = (UInt16)(this.opcode & 0x0FFF);
                    this.pc += 2;
                }

                // 0xB... -> Jumps to address ... + V0
                else if (codefmask == 0xB000)
                {
                    this.pc = (UInt16)((this.opcode & 0x0FFF) + this.v[0]);
                }

                // 0xCXMM -> Set VX to random byte with mask MM
                else if (codefmask == 0xC000)
                {
                    byte vx = (byte)((this.opcode & 0x0F00) >> 8);
                    this.v[vx] = (byte)((this.rand.Next(0xFF) % 0xFF) & (opcode & 0x00FF));
                    this.pc += 2;
                }

                // 0xDXYN -> Display memory image with first address in registry I 
                // and length N bytes, on coordinates X, Y. One byte x line.
                else if (codefmask == 0xD000)
                {
                    byte vx = (byte)((this.opcode & 0x0F00) >> 8);
                    byte vy = (byte)((this.opcode & 0x00F0) >> 4);
                    this.Instruction0xDXYN(this.v[vx], this.v[vy], (UInt16)(this.opcode & 0x000F));
                }

                // 0xE...
                else if (codefmask == 0xE000)
                {
                    byte vx = (byte)((this.opcode & 0x0F00) >> 8);

                    switch (opcode & 0x00FF)
                    {
                        // 0xEX9E -> Skip next instruction if VX = Key pressed
                        case 0x009E:
                            this.pc += (UInt16)((this.v[vx] != 0) ? 4 : 2); // FIXME Del revés?
                            break;

                        // 0xEXA1 -> Skip next instruction if VX != Key pressed
                        case 0x00A1:
                            this.pc += (UInt16)((this.v[vx] == 0) ? 4 : 2); // FIXME Del revés?
                            break;

                        default:
                            Console.WriteLine("OPCode desconocido: " + this.opcode.ToString("X4"));
                            return false;
                    }
                }

                // 0xF...
                else if (codefmask == 0xF000)
                {
                    byte vx = (byte)((this.opcode & 0x0F00) >> 8);

                    switch (opcode & 0x00FF)
                    {
                        // 0xFX07 -> Sets VX to the value of the delay timer
                        case 0x0007:
                            this.v[vx] = this.timer_delay;
                            this.pc += 2;
                            break;

                        // 0xFX0A -> A key press is awaited, and then stored in VX	
                        case 0x000A:
                            bool press = false;

                            for (int i = 0; i < this.key.Length; i++)
                            {
                                if (this.key[i] != 0)
                                {
                                    this.v[vx] = (byte)i;
                                    press = true;
                                }
                            }

                            // If key not press, wait other clock cicle.
                            if (!press) return true;

                            this.pc += 2;
                            break;

                        // 0xFX15 -> Sets the delay timer to VX
                        case 0x0015:
                            this.timer_delay = this.v[vx];
                            this.pc += 2;
                            break;

                        // 0xFX18 -> Sets the sound timer to VX
                        case 0x0018:
                            this.timer_sound = this.v[vx];
                            this.pc += 2;
                            break;

                        // 0xFX1E -> Adds VX to I
                        case 0x001E:
                            this.v[0xf] = (byte)((this.i + this.v[vx] > 0xfff) ? 1 : 0);
                            this.i = this.v[vx];
                            this.pc += 2;
                            break;

                        // 0xFX29 -> Sets I to the location of the sprite for the character in VX. Characters 0-F (in hexadecimal) are represented by a 4x5 font
                        case 0x0029:
                            this.i = (UInt16)(this.v[vx] * 0x5);
                            this.pc += 2;
                            break;

                        // 0xFX33 -> Stores the Binary-coded decimal representation of VX at the addresses I, I plus 1, and I plus 2
                        case 0x0033:
                            this.memory[this.i] = (byte)(this.v[vx] / 100);
                            this.memory[this.i + 1] = (byte)((this.v[vx] / 10) % 10);
                            this.memory[this.i + 2] = (byte)((this.v[vx] % 100) % 10);
                            this.pc += 2;
                            break;

                        // 0xFX55 -> Stores V0 to VX in memory starting at address I
                        case 0x0055:
                            for (int j = 0; j <= vx; j++)
                            {
                                this.memory[this.i + j] = this.v[j];
                            }

                            // On the original interpreter, when the operation is done, I = I + X + 1.
                            this.i += (UInt16)(vx + 1);

                            this.pc += 2;
                            break;

                        // 0xFX65 -> Fills V0 to VX with values from memory starting at address I
                        case 0x0065:
                            for (int j = 0; j <= vx; j++)
                            {
                                this.v[j] = this.memory[this.i + j];
                            }

                            // On the original interpreter, when the operation is done, I = I + X + 1.
                            this.i += (UInt16)(vx + 1);

                            this.pc += 2;
                            break;

                        default:
                            Console.WriteLine("OPCode desconocido 2: " + this.opcode.ToString("X4"));
                            return false;
                    }
                }

                else
                {
                    Console.WriteLine("OPCode desconocido 1: " + this.opcode.ToString("X4"));
                    return false;
                }
            }

            return true;
        }

        private void Instruction0xDXYN(UInt16 x, UInt16 y, UInt16 lines)
        {
            this.v[0xf] = 0;

            byte pixelv;
            int vmemoryi;

            // For lines on axis y
            for (int iy = 0; iy < lines; iy++)
            {
                // Get 8 pixel boolean values
                pixelv = this.memory[this.i + iy];

                // Fetch 8 pixels
                for (int ix = 0; ix < 8; ix++)
                {
                    // If pixel value "true"
                    if ((pixelv & (0x80 >> ix)) != 0)
                    {
                        vmemoryi = x + ix + ((y + iy) * 64);
                        if (this.video[vmemoryi] == 1) { this.v[0xF] = 1; }
                        this.video[vmemoryi] ^= 1; // FIXME
                    }
                }
            }

            this.pc += 2;
            if (this.ScreenChanged != null) ScreenChanged(this, this.video);
        }

        public UInt32 ClockFrequency
        {
            get { return this.clock_cpu_hz; }
        }

        public static Chip8 LoadROM(String path)
        {
            Chip8 toret = new Chip8();
            return Chip8.LoadROM(File.ReadAllBytes(path));
        }

        public static Chip8 LoadROM(byte[] rom)
        {
            Chip8 toret = new Chip8();

            if (rom.Length < 4096 - 512)
            {
                Buffer.BlockCopy(rom, 0, toret.memory, 512, rom.Length);
            }
            else
            {
                throw new OutOfMemoryException("ROM size is too big.");
            }

            return toret;
        }

        public delegate void ScreenChangedEventHandler(object sender, byte[] video);
    }
}
