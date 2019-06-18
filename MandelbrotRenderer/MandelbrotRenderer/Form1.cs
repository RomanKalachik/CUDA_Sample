using Hybridizer.Runtime.CUDAImports;
using MandelbrotRenderer.Mandelbrots;
using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Management;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace MandelbrotRenderer
{
    public partial class Form1 : Form
    {
        enum Flavors
        {
            //Source,
            //AVX,
            //AVX2,
            //AVX512,
            CUDA
        }

        public byte[,] output;
        Bitmap image { get; set; }
        int maxiter = 50;
        float fromX = -1.0F;
        float fromY = -0.30F;
        float sX = 4.0F;
        float sY = 4.0F;

        HybRunner runnerAVX;
        HybRunner runnerAVX2;
        HybRunner runnerAVX512;
        HybRunner runnerCUDA;
        dynamic MandelbrotAVX;
        dynamic MandelbrotAVX2;
        dynamic MandelbrotAVX512;
        dynamic MandelbrotCUDA;

        Flavors flavor = Flavors.CUDA;

        const int W = 1024;
        const int H = 1024;

        bool hasCUDA = true;
        bool hasAVX = false;
        bool hasAVX2 = false;
        bool hasAVX512 = false;

        public Form1()
        {
            InitializeComponent();
            CUDA.Enabled = hasCUDA;
            AVX.Enabled = hasAVX;
            AVX2.Enabled = hasAVX2;
            AVX512.Enabled = hasAVX512;

            if (hasCUDA)
            {
                DisplayGPUName();
            }

            ManagementObjectSearcher mos = new ManagementObjectSearcher("root\\CIMV2", "SELECT * FROM Win32_Processor");
            foreach (ManagementObject mo in mos.Get())
            {
                string cpuName = (string)mo["Name"];
                label4.Text = cpuName.Split('@')[0];
            }

            if (hasCUDA) runnerCUDA = HybRunner.Cuda("MandelbrotRenderer_CUDA.dll").SetDistrib(32, 32, 16, 16, 1, 0);
            if (hasAVX) runnerAVX = HybRunner.AVX("MandelbrotRenderer_AVX.dll").SetDistrib(Environment.ProcessorCount, 32);
            if (hasAVX2) runnerAVX2 = HybRunner.AVX("MandelbrotRenderer_AVX2.dll").SetDistrib(Environment.ProcessorCount, 32);
            if (hasAVX512) runnerAVX512 = HybRunner.AVX512("MandelbrotRenderer_AVX512.dll").SetDistrib(Environment.ProcessorCount, 32);

            if (hasCUDA) MandelbrotCUDA = runnerCUDA.Wrap(new Mandelbrot());
            if (hasAVX) MandelbrotAVX = runnerAVX.Wrap(new Mandelbrot());
            if (hasAVX2) MandelbrotAVX2 = runnerAVX2.Wrap(new Mandelbrot());
            if (hasAVX512) MandelbrotAVX512 = runnerAVX512.Wrap(new Mandelbrot());

            this.FormBorderStyle = FormBorderStyle.FixedSingle;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            image = new Bitmap(W, H, PixelFormat.Format32bppRgb);
            Rendering.Image = image;
            render();
            Rendering.MouseDown += (s, e) => { ImageOnMouseDown(e); };
            Rendering.MouseMove += (s, e)=>  { ImageOnMouseMove(e); };

        }

        private void DisplayGPUName()
        {
            int deviceCount;
            cuda.GetDeviceCount(out deviceCount);
            if (deviceCount == 0)
            {
                MessageBox.Show("no CUDA capable device detected -- do not try to use the CUDA version!");
            }

            int major = 0;
            int selectedDevice = 0;
            int mp = 0;
            string deviceName = string.Empty;
            for (int i = 0; i < deviceCount; ++i)
            {
                cudaDeviceProp prop;
                cuda.GetDeviceProperties(out prop, i);
                if (prop.major > major)
                {
                    selectedDevice = i;
                    major = prop.major;
                    mp = prop.multiProcessorCount;
                    deviceName = new string(prop.name);
                }
            }

            cuda.SetDevice(selectedDevice);

            label5.Text = deviceName;
        }

        private void render()
        {
            int[] iterCount = new int[W * H];

            Stopwatch watch = new Stopwatch();
            long elapsedMilliseconds;

            watch.Start();
            switch (flavor)
            {
                //case Flavors.AVX:
                //    MandelbrotAVX.Render(iterCount, fromX, fromY, sX, sY, 1.0F / W, 1.0F / H, H, W, 0, H, maxiter);
                //    break;
                //case Flavors.AVX2:
                //    MandelbrotAVX2.Render(iterCount, fromX, fromY, sX, sY, 1.0F / W, 1.0F / H, H, W, 0, H, maxiter);
                //    break;
                //case Flavors.AVX512:
                //    MandelbrotAVX512.Render(iterCount, fromX, fromY, sX, sY, 1.0F / W, 1.0F / H, H, W, 0, H, maxiter);
                //    break;
                case Flavors.CUDA:
                //case Flavors.Source:
                default:
                    MandelbrotCUDA.Render2D(iterCount, fromX, fromY, sX, sY, 1.0F / W, 1.0F / H, H, W, maxiter);
                    cuda.DeviceSynchronize();
                    break;
               
                    //int slice = H / Environment.ProcessorCount;
                    //Parallel.For(0, Environment.ProcessorCount, tid =>
                    //{
                    //    int lineFrom = tid * slice;
                    //    int lineTo = Math.Min(H, lineFrom + slice);
                    //    Mandelbrots.Mandelbrot.Render(iterCount, fromX, fromY, sX, sY, 1.0F / W, 1.0F / H, H, W, lineFrom, lineTo, maxiter);
                    //});
                    //break;
            }
            watch.Stop();
            BitmapData data = image.LockBits(new Rectangle(0, 0, W, H), ImageLockMode.ReadWrite, PixelFormat.Format24bppRgb);
            int stride = data.Stride;
            unsafe
            {
                byte* ptr = (byte*)data.Scan0;
                Rectangle areaToPaint = new Rectangle(0, 0, W,H);

                if (!areaToPaint.IsEmpty)
                {
                    for (int y = areaToPaint.Top; y < areaToPaint.Bottom; y++)
                    {
                        for (int x = areaToPaint.Left; x < areaToPaint.Right; x++)
                        {
                            int color = GetMandelbrotColor(iterCount[y * W + x], maxiter);
                            var m_colour = Color.FromArgb(color);
                            ptr[(x * 3) + y * stride] = m_colour.B;
                            ptr[(x * 3) + y * stride + 1] = m_colour.G;
                            ptr[(x * 3) + y * stride + 2] = m_colour.R;
                        }
                    }
                }
            }
            image.UnlockBits(data);
            elapsedMilliseconds = watch.ElapsedMilliseconds;
            if (flavor == Flavors.CUDA)
            {
                elapsedMilliseconds = runnerCUDA.LastKernelDuration.ElapsedMilliseconds;
            }

            label2.Text = "Rendering Time : " + watch.ElapsedMilliseconds + " ms";

            Rendering.Invalidate();
        }

        private static int GetMandelbrotColor(int iterCount, int maxiter)
        {
            if (iterCount == maxiter)
            {
                return 0;
            }

            return ((int)(iterCount * (255.0F / (float)(maxiter - 1)))) << 8;
        }

        private void RenderButton_Click(object sender, EventArgs e)
        {
            render();
        }

        private void MaxiterInput_ValueChanged(object sender, EventArgs e)
        {
            if (sender is NumericUpDown)
            {
                NumericUpDown ud = sender as NumericUpDown;
                maxiter = (int)ud.Value;
            }
            render();
        }

        private void FlavorCheckedChanged(object sender, EventArgs e)
        {
            foreach (Control control in this.Flavor.Controls)
            {
                if (control is RadioButton)
                {
                    RadioButton radio = control as RadioButton;
                    if (radio.Checked)
                    {
                        switch (radio.Name.ToLowerInvariant())
                        {
                            //case "avx":
                            //    flavor = Flavors.AVX;
                            //    break;
                            //case "avx2":
                            //    flavor = Flavors.AVX2;
                            //    break;
                            //case "avx512":
                            //    flavor = Flavors.AVX512;
                            //    break;
                            case "cuda":
                            case "csharp":
                            default:
                                flavor = Flavors.CUDA;
                                break;
                          
                                //flavor = Flavors.Source;
                                //break;
                        }
                        render();
                    }
                }
            }
        }
        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            if (e.Delta > 0)
                Rendering_Click(null, new MouseEventArgs(MouseButtons.Left, e.X, e.Y, 0, 0));
            else
                Rendering_Click(null, new MouseEventArgs(MouseButtons.Right, e.X, e.Y, 0, 0));
        }
        Point mdPoint = Point.Empty;
        PointF startPos;
        protected void ImageOnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            mdPoint = new Point(e.X, e.Y);
            startPos = new PointF(fromX, fromY);
        }
        protected void ImageOnMouseMove(MouseEventArgs e)
        {
            //base.OnMouseMove(e);
            if (e.Button == MouseButtons.Left) {
                float dx, dy;
                dx = e.X - mdPoint.X;
                dy = e.Y - mdPoint.Y;
                fromX = startPos.X - dx*sX/100;
                fromY = startPos.Y - dy*sX/100;

                render();
            }
            
        }
        private void Rendering_Click(object sender, EventArgs e)
        {
            MouseEventArgs mevent = e as MouseEventArgs;
            if (mevent.Button == MouseButtons.Left)
            {
                if (sX / W <= 1.0E-8)
                {
                    MessageBox.Show("float precision limit -- we didn't implement infinite zoom !");
                    return;
                }
            }
            int x = mevent.X;
            int y = mevent.Y;
            float fx = fromX + sX * ((float)x / Rendering.Width);
            float fy = fromY + sY * ((float)y / Rendering.Height);
            if (mevent.Button == MouseButtons.Left)
            {
                sX *= 0.95F;
                sY *= 0.95F;
            }
            else if (mevent.Button == MouseButtons.Right)
            {
                sX *= 1.05F;
                sY *= 1.05F;
            }

            label3.Text = "Size : " + string.Format("{0:0.##E+00}", sX);
            render();
        }

        private void Info_Click(object sender, EventArgs e)
        {
            MessageBox.Show($"Render\n\tFrom: [{fromX}, {fromY}]\n\tSize: [{sX}, {sY}]\n\tIterations : {maxiter})", "Render Parameters", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
    }
}
