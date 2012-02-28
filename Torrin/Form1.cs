using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using System.IO.Ports;


// Command structure for the RS232 interface
// each command is a 4 byte transfer
// TX command
// RX data
// TX data
// RX status

// You send your command byte, and get a data byte back, i
// if your command accepts data now  send it or send 0, then get a status byte back

// This talks with the ifm02/VI card, to set parameters you need to use the SetAddr and Read/Write byte functions

// In the VI the first memory/RAM location is at 0xe000 this location is a pointer to the real paramater table so the first thing
// to do is get this address, this is the base address

// Memory in the VI is 16bit so you need to do two reads/writes for every location to handle high and lo bytes, the endian is LOW,HIGH
// so the first memory location is the lowbyte the 2nd is the highbyte

// to get a paramater you look up the memory location in the N101_ptr_table (this lives at the address pointed to by memory 0xe000)
// so index into this table by 2x the paramater number, this gives you the actual memory location to read/write for the parameter.

// This should all be abstracted by the code below so all you need to use is getpar() and setpar() to get/set changes

/*
x99_cmd_tab:
        .dw     x99_null_c      ; 0:Null
        .dw     x99_mod1_c      ; 1:Mode1 (same as null)
        .dw     x99_adlo_c      ; 2:SetAddrLo
        .dw     x99_adhi_c      ; 3:SetAddrHi
        .dw     x99_rdby_c      ; 4:Read byte
        .dw     x99_wrby_c      ; 5:Write byte
        .dw     x99_null_c      ; 6:
        .dw     x99_null_c      ; 7:
        .dw     x99_sdlo_c      ; 8:Set data lo
        .dw     x99_sdhi_c      ; 9:Set data hi
        .dw     x99_gdlo_c      ;10:Get data lo
        .dw     x99_gdhi_c      ;11:Get data hi
 */ 
/*
N101_ptr_table:
                .dw     N101_V1         ;0  Fixture 1 voltage
                .dw     N101_V2         ;1  Fixture 2 voltage
                .dw     N101_V3         ;2  Fixture 3 voltage
                .dw     N101_V4         ;3  Fixture 4 voltage

                .dw     N101_L162       ;4  62mm Lower Limit Fix 1
                .dw     N101_U162       ;5  62mm Upper Limit Fix 1
                .dw     N101_R162       ;6  62mm Range Fix 1
                .dw     N101_L187       ;7  87mm Lower Limit Fix 1
                .dw     N101_U187       ;8  87mm Upper Limit Fix 1
                .dw     N101_R187       ;9  87mm Range Fix 1
                .dw     N101_L1107      ;10 107mm Lower Limit Fix 1
                .dw     N101_U1107      ;11 107mm Upper Limit Fix 1
                .dw     N101_R1107      ;12 107mm Range Fix 1

                .dw     N101_L244       ;13 44mm Lower Limit Fix 2
                .dw     N101_U244       ;14 44mm Upper Limit Fix 2
                .dw     N101_R244       ;15 44mm Range Fix 2
                .dw     N101_L242       ;16 42mm Lower Limit Fix 2
                .dw     N101_U242       ;17 42mm Upper Limit Fix 2
                .dw     N101_R242       ;18 42mm Range Fix 2
                .dw     N101_L2S30      ;19 Short 30mm Lower Limit Fix 2
                .dw     N101_U2S30      ;20 Short 30mm Upper Limit Fix 2
                .dw     N101_R2S30      ;21 Short 30mm Range Fix 2
                .dw     N101_L2L30      ;22 Long 30mm Lower Limit Fix 2
                .dw     N101_U2L30      ;23 Long 30mm Upper Limit Fix 2
                .dw     N101_R2L30      ;24 Long 30mm Range Fix 2

                .dw     N101_L3S        ;25 Short Lower Limit Fix 3
                .dw     N101_U3S        ;26 Short Upper Limit Fix 3
                .dw     N101_R3S        ;27 Short Range Fix 3
                .dw     N101_L3L        ;28 Long Lower Limit Fix 3
                .dw     N101_U3L        ;29 Long Upper Limit Fix 3
                .dw     N101_R3L        ;30 Long Range Fix 3

                .dw     N101_L4S        ;31 Short Lower Limit Fix 4
                .dw     N101_U4S        ;32 Short Upper Limit Fix 4
                .dw     N101_R4S        ;33 Short Range Fix 4
                .dw     N101_L4L        ;34 Long Lower Limit Fix 4
                .dw     N101_U4L        ;35 Long Upper Limit Fix 4
                .dw     N101_R4L        ;36 Long Range Fix 4
*/
namespace Torrin
{
    public partial class Form1 : Form
    {
        SerialPort port;

        int baseaddr;

        public Form1()
        {
            InitializeComponent();
            numericUpDown_value.Enabled = false;
            numericUpDown_parameter.Enabled = false;
            button3.Enabled = false;
            button4.Enabled = false;

        }

        private void Form1_Load(object sender, EventArgs e)
        {
           
            string[] theSerialPortNames = System.IO.Ports.SerialPort.GetPortNames();
            foreach (string port in theSerialPortNames)
            {
                comboBox1.Items.Add(port);
            }

        }

        private void button1_Click(object sender, EventArgs e)
        {
            if (port != null && port.IsOpen)
                port.Close();

            textBox1.Clear();
            textBox_dataout.Clear();

            port = new SerialPort(comboBox1.SelectedItem.ToString(), 9600, Parity.None, 8, StopBits.One);

            port.Open();

            writemsg("Port open sending *");
            byte ret = (byte)1;
            while (ret != (byte)0)
            {
                handshake(42, out ret);
                System.Threading.Thread.Sleep(1000);
              
            }
            if (!comstest())
                return;


            writemsg(string.Format("Getting base address"));

            baseaddr = fromHL(readmem(0xe001), readmem(0xe000));

            writemsg(String.Format("Base address = {0:X}",baseaddr));

            writemsg("Getting Parameters");


            // raw read test 0x4353 is curlev in MCSD100J torin hex
            /*
            byte L = readmem(0x4353);
            byte H = readmem(0x4353+1);
            int test = fromHL(H, L);
            textBox1.Text += string.Format("memory 0x{0:X} is {1}\r\n", 0x4353, test);
            
             * 
            // conversion test check our byte splitting works
            int t = 400;  
            toHL(t, out H, out L);
            t = fromHL(H, L);
            
             */

            /*
            baseaddr = 0x4353;
            //curlev test only valid with MCSD 100J
            
            setpar(0, 100);

            int result = getpar(0); ;
            textBox1.Text += string.Format("memory 0x{0:X} is {1}\r\n", 0x4353, result);

            setpar(0, 400);
            result = getpar(0); ;

            textBox1.Text += string.Format("Memory 0x{0:X} is {1}\r\n", 0x4353, result);
            */


            for (int p = 0; p < 36; p++)
            {
                int val = getpar(p);
                textBox1.Text += string.Format("Parameter {0} is {1}\r\n", p, val);
           }

            numericUpDown_value.Enabled = true;
            numericUpDown_parameter.Enabled = true;
            button3.Enabled = true;
            button4.Enabled = true;


        }

        // Print a debug message to the text box
        private void writemsg(string msg)
        {
            textBox_dataout.Text += msg + "\r\n";
        }


 
        // convert to/from 16bit to a high and low byte
        void toHL(int data, out byte h, out byte l)
        {
            h = (byte)(0x00FF & (data >> 8));
            l = (byte)(0x00FF & data);
        }

        int fromHL(byte h, byte l)
        {
            int data = h * 256 + l;
            return data;
        }

    
        // Do a packet handshake, Send a byte and recieve a byte
        bool handshake(byte dataout,out byte reply)
        {
            reply = 0;

            if (port == null)
                return false;

            byte [] dout = new byte[1];
            dout[0] = dataout;

            byte[] ret = new byte[1];

            port.Write(dout,0,1);
            writemsg(string.Format("TX {0:X}",(int)dataout));
            Application.DoEvents();
            port.ReadTimeout = 5000;

            try
            {
                port.Read(ret, 0, 1);
                reply = ret[0];
                int x = (int)reply;
                writemsg(string.Format("RX {0:X}", x));
                Application.DoEvents();
            }
            catch
            {
                  writemsg("RX TIMEOUT");
                return false;
            }

            System.Threading.Thread.Sleep(100);


            return true;
        }

        // two a 4 byte transfer
        bool docmd(byte cmd, byte data, out byte dret, out byte sret)
        {
            bool ok = handshake(cmd, out dret);
            ok &= handshake(data, out sret);
            return ok;
        }

        // write to a single (8 byte) memory location
        void writemem(int addr, byte value)
        {
            byte hi, lo;
            byte dret, sret;

            toHL(addr, out hi, out lo);

            docmd((byte)2, (byte)lo, out dret, out sret); //set datalo
            docmd((byte)3, (byte)hi, out dret, out sret); //setdata hi
            docmd((byte)5, (byte)value, out dret, out sret); //write byte

        }

        // read from a single (8 byte) memory location
        byte readmem(int addr)
        {
            byte hi, lo;
            byte dret, sret;

            toHL(addr, out hi, out lo);

            docmd((byte)2, (byte)lo, out dret, out sret); //set datalo
            docmd((byte)3, (byte)hi, out dret, out sret); //setdata hi
            docmd((byte)4, (byte)0, out dret, out sret); //read byte

            return (byte)dret;
        }

        // get a full 16bit value
        int getmem(int addr)
        {
            byte lo = readmem(addr);
            byte hi = readmem(addr+1);
            return fromHL(hi, lo);
        }

        //set a full 16 bit value
        void setmem(int addr, int value)
        {
            byte H, L;
            toHL(value, out H, out L);
            writemem(addr + 1, H);
            writemem(addr, L);
         
        }

        // get a parameter from the table
        int getpar(int num)
        {
            int ad = baseaddr + num * 2;
            ad = fromHL(readmem(ad + 1),readmem(ad));

            writemsg(string.Format("reading par {0} Offset address is {1}",num,ad));

            int val = getmem(ad);

            writemsg(string.Format("par {0} is {1}", num, val));

            return val;
        }

        // set a paramater in the sable
        void setpar(int num, int value)
        {
            int ad = baseaddr + num * 2;
            ad = fromHL(readmem(ad + 1), readmem(ad));

            setmem(ad, value);

        }

        // basic test to ensure we can set and get data
        bool comstest()
        {
            textBox_dataout.Text += "COMS TEST\r\n";

            byte retdata, retstatus;

            handshake((byte)8, out retdata);
            handshake((byte)0x90, out retstatus);

            handshake((byte)10, out retdata);
            handshake((byte)0, out retstatus);

            if (retdata == 0x90)
            {
                writemsg("PASS");
                return true;
            }
            else
            {
                writemsg("FAIL");
                return false;
            }

        }

        private void button2_Click(object sender, EventArgs e)
        {
            byte L = readmem(0x4353);
            byte H = readmem(0x4353 + 1);
            int test = fromHL(H, L);
            textBox1.Text += string.Format("memory 0x{0:X} is {1}\r\n", 0x4353, test);
        }

        private void button3_Click(object sender, EventArgs e)
        {
            int parameter = (int)numericUpDown_parameter.Value;
            int value = (int)numericUpDown_value.Value;

            writemsg(string.Format("\nWriting paramater {0} value {1}", parameter, value));


            setpar(parameter, value);

            writemsg(string.Format("Write done \n"));

       
        }

        private void button4_Click(object sender, EventArgs e)
        {

            writemsg(string.Format("\n Saving to E2"));

            setpar(39, 0); //saving handshake register
            byte dret, sret;
            docmd(41, 0, out dret, out sret);

            while (getpar(39) == 0)
            {
                writemsg(string.Format("\n Waiting for OK handshake"));
                System.Threading.Thread.Sleep(1000);
            }

            writemsg(string.Format("\n Save complete!"));
        }

    }
}
