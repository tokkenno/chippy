using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Data;
using System.Text;
using System.Windows.Forms;

namespace Chippy
{
    public partial class Chip8Screen : PictureBox
    {
        private Chip8 chip;

        public Chip8Screen() { }

        public Chip8Screen(Chip8 chip)
        {
            this.chip = chip;
            this.chip.ScreenChanged += new Chip8.ScreenChangedEventHandler(this.OnScreenChanged);
        }

        void OnScreenChanged(object sender, byte[] video)
        {
            Bitmap B = new Bitmap(this.Width, this.Height, System.Drawing.Imaging.PixelFormat.Format24bppRgb);
            Graphics G = Graphics.FromImage(B);

            G.Clear(Color.Black);

            for (int x = 0; x < 32; x++)
            {
                for (int y = 0; y < 64; y++)
                {
                    byte elemento = video[(x * 64) + y];

                    if (elemento != 0)
                    {
                        this.printPixel(G, x, y, Color.Aquamarine);
                    }
                }
            }

            this.Image = B;
        }

        private void printPixel(Graphics G, int Y, int X, Color C)
        {
            int x = (X * (this.Width / 64));
            int y = (Y * (this.Height / 32));
            SolidBrush Br = new SolidBrush(C);
            G.FillRectangle(Br, x, y, (this.Width / 64), (this.Height / 32));
        }
    }
}
