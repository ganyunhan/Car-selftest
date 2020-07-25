using CCWin;
using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Data.SqlClient;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using ZLGCAN;
using ZLGCANDemo;

namespace CAN_SelfTest
{
    
    public partial class Form1 : Skin_Metro
    {
        /*定义变量*/
        const int NULL = 0;
        const int CANFD_BRS = 0x01; /* bit rate switch (second bitrate for payload data) */
        const int CANFD_ESI = 0x02; /* error state indicator of the transmitting node */

        /* CAN payload length and DLC definitions according to ISO 11898-1 */
        const int CAN_MAX_DLC = 8;
        const int CAN_MAX_DLEN = 8;

        /* CAN FD payload length and DLC definitions according to ISO 11898-7 */
        const int CANFD_MAX_DLC = 15;
        const int CANFD_MAX_DLEN = 64;

        const uint CAN_EFF_FLAG = 0x80000000U; /* EFF/SFF is set in the MSB */
        const uint CAN_RTR_FLAG = 0x40000000U; /* remote transmission request */
        const uint CAN_ERR_FLAG = 0x20000000U; /* error message frame */
        const uint CAN_ID_FLAG = 0x1FFFFFFFU; /* id */

        DeviceInfo[] kDeviceType =
        {
            new DeviceInfo(Define.ZCAN_USBCAN1, 1),
            new DeviceInfo(Define.ZCAN_USBCAN2, 2),
            new DeviceInfo(Define.ZCAN_USBCAN_E_U, 1),
            new DeviceInfo(Define.ZCAN_USBCAN_2E_U, 2),
            new DeviceInfo(Define.ZCAN_PCIECANFD_100U, 1),
            new DeviceInfo(Define.ZCAN_PCIECANFD_200U, 2),
            new DeviceInfo(Define.ZCAN_PCIECANFD_400U, 4),
            new DeviceInfo(Define.ZCAN_USBCANFD_200U, 2),
            new DeviceInfo(Define.ZCAN_USBCANFD_100U, 1),
            new DeviceInfo(Define.ZCAN_USBCANFD_MINI, 1),
            new DeviceInfo(Define.ZCAN_CANETTCP, 1),
            new DeviceInfo(Define.ZCAN_CANETUDP, 1),
            new DeviceInfo(Define.ZCAN_CLOUD, 1)
        };

        byte[] kTiming0 =
        {
            0x00, //1000kbps
            0x00, //800kbps
            0x00, //500kbps
            0x01, //250kbps
            0x03, //125kbps
            0x04, //100kbps
            0x09, //50kbps
            0x18, //20kbps
            0x31, //10kbps
            0xBF  //5kbps
        };
        byte[] kTiming1 =
        {
            0x14,//1000kbps
            0x16,//800kbps
            0x1C,//500kbps
            0x1C,//250kbps
            0x1C,//125kbps
            0x1C,//100kbps
            0x1C,//50kbps
            0x1C,//20kbps
            0x1C,//10kbps
            0xFF //5kbps
        };
        uint[] kBaudrate =
        {
            1000000,//1000kbps
            800000,//800kbps
            500000,//500kbps
            250000,//250kbps
            125000,//125kbps
            100000,//100kbps
            50000,//50kbps
            20000,//20kbps
            10000,//10kbps
            5000 //5kbps
        };

        public string fName;
        private int channel_index_;
        IntPtr device_handle_;
        IntPtr channel_handle_;
        IProperty property_;
        recvdatathread recv_data_thread_;
        string list_box_data_;
        private int[] index;
        uint serial = 1;
        
        bool m_bOpen = false;
        bool m_bStart = false;
        bool m_bCloud = false;
        bool open_device_flag = false;

        public int Channel_index_ { get => channel_index_; set => channel_index_ = value; }

        public Form1()
        {
            InitializeComponent();
            this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedDialog;
        }


        /*窗体程序初始化设置*/
        private void Form1_Load(object sender, EventArgs e)
        {
            Device_type.SelectedIndex = 0;
            Working_Mode.SelectedIndex = 0;
            CAN_type.SelectedIndex = 0;
            Trans_Mode.SelectedIndex = 0;
            Baundrate.SelectedIndex = 0;
            trans_long.SelectedIndex = 0;
            Device_index.SelectedIndex = 0;
            COM_Num.SelectedIndex = 0;

            Frame_ID.Text = "0101";
            data_trans.Text = "FE 11 22 33 44 55 66 77";

            Version_Num.Text = "1.0.1";
            Device_SerialNum.Text = "Unknown";
            Hardware_type.Text = "Unknown";
        }
        public uint MakeCanId(uint id, int eff, int rtr, int err)//1:extend frame 0:standard frame
        {
            uint ueff = (uint)(!!(Convert.ToBoolean(eff)) ? 1 : 0);
            uint urtr = (uint)(!!(Convert.ToBoolean(rtr)) ? 1 : 0);
            uint uerr = (uint)(!!(Convert.ToBoolean(err)) ? 1 : 0);
            return id | ueff << 31 | urtr << 30 | uerr << 29;
        }

        public bool IsEFF(uint id)//1:extend frame 0:standard frame
        {
            return !!Convert.ToBoolean((id & CAN_EFF_FLAG));
        }

        public bool IsRTR(uint id)//1:remote frame 0:data frame
        {
            return !!Convert.ToBoolean((id & CAN_RTR_FLAG));
        }

        public bool IsERR(uint id)//1:error frame 0:normal frame
        {
            return !!Convert.ToBoolean((id & CAN_ERR_FLAG));
        }

        public uint GetId(uint id)
        {
            return id & CAN_ID_FLAG;
        }
        

        //窗体程序结束操作
        private void Form1_FormClosed(object sender, FormClosedEventArgs e)
        {
            if (m_bOpen)
            {
                Method.ZCAN_CloseDevice(device_handle_);
            }
            if (Method.ZCLOUD_IsConnected())
            {
                Method.ZCLOUD_DisconnectServer();
            }
        }

        //打开设备按钮触发事件
        private void Open_Device_Click_1(object sender, EventArgs e)
        {
            if(!open_device_flag)
            {
                uint device_type_index_ = (uint)Device_type.SelectedIndex;
                uint device_index_;
                device_index_ = (uint)Device_index.SelectedIndex;
                device_handle_ = Method.ZCAN_OpenDevice(kDeviceType[device_type_index_].device_type, device_index_, 0);
                if (NULL == (int)device_handle_)
                {
                    Open_Device.Enabled = true;
                    MessageBox.Show("打开设备失败", "错误",
                            MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }
                else
                {
                    open_device_flag = true;
                    Open_Device.Text = "关闭设备";
                    m_bOpen = true;
                    return;
                }
            }
            else
            {
                Method.ZCAN_CloseDevice(device_handle_);
                Open_Device.Text = "打开设备";
                MessageBox.Show("关闭设备成功", "提示",
                            MessageBoxButtons.OK, MessageBoxIcon.Information);
                open_device_flag = false;
                Init_CAN.Enabled = true;
                Start_CAN.Enabled = true;
                return;
            }
        }

        //初始化按钮触发事件
        private void Init_CAN_Click(object sender, EventArgs e)
        {
            if (!m_bOpen)
            {
                MessageBox.Show("设备未被打开", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            uint type = kDeviceType[Device_type.SelectedIndex].device_type;
            bool netDevice = type == Define.ZCAN_CANETTCP || type == Define.ZCAN_CANETUDP;
            bool pcieCanfd = type == Define.ZCAN_PCIECANFD_100U ||
                type == Define.ZCAN_PCIECANFD_200U ||
                type == Define.ZCAN_PCIECANFD_400U;
            bool usbCanfd = type == Define.ZCAN_USBCANFD_100U ||
                type == Define.ZCAN_USBCANFD_200U ||
                type == Define.ZCAN_USBCANFD_MINI;
            bool canfdDevice = usbCanfd || pcieCanfd;
            if (!m_bCloud)
            {
                IntPtr ptr = Method.GetIProperty(device_handle_);
                if (NULL == (int)ptr)
                {
                    MessageBox.Show("设置指定路径属性失败", "提示", MessageBoxButtons.OK, MessageBoxIcon.Exclamation);
                    return;
                }

                property_ = (IProperty)Marshal.PtrToStructure((IntPtr)((UInt32)ptr), typeof(IProperty));
                 if (!canfdDevice && !setBaudrate(kBaudrate[Baundrate.SelectedIndex]))
                 {
                       MessageBox.Show("设置波特率失败", "提示", MessageBoxButtons.OK, MessageBoxIcon.Exclamation);
                       return;
                 }
            }
            

            ZCAN_CHANNEL_INIT_CONFIG config_ = new ZCAN_CHANNEL_INIT_CONFIG();
            if (!m_bCloud && !netDevice)
            {
                config_.canfd.mode = (byte)Working_Mode.SelectedIndex;
                config_.can_type = Define.TYPE_CAN;
                config_.can.timing0 = kTiming0[Baundrate.SelectedIndex];
                config_.can.timing1 = kTiming1[Baundrate.SelectedIndex];
                config_.can.filter = 0;
                config_.can.acc_code = 0;
                config_.can.acc_mask = 0xFFFFFFFF;
            }
            IntPtr pConfig = Marshal.AllocHGlobal(Marshal.SizeOf(config_));
            Marshal.StructureToPtr(config_, pConfig, true);

            //int size = sizeof(ZCAN_CHANNEL_INIT_CONFIG);
            //IntPtr ptr = System.Runtime.InteropServices.Marshal.AllocCoTaskMem(size);
            //System.Runtime.InteropServices.Marshal.StructureToPtr(config_, ptr, true);
            channel_handle_ = Method.ZCAN_InitCAN(device_handle_, (uint)Channel_index_, pConfig);
            Marshal.FreeHGlobal(pConfig);

            //Marshal.FreeHGlobal(ptr);

            if (NULL == (int)channel_handle_)
            {
                MessageBox.Show("初始化CAN失败", "提示", MessageBoxButtons.OK, MessageBoxIcon.Exclamation);
                return;
            }
            Init_CAN.Enabled = false;
        }

        //设置波特率
        private bool setBaudrate(UInt32 baud)
        {
            string path = Channel_index_ + "/baud_rate";
            string value = baud.ToString();
            //char* pathCh = (char*)System.Runtime.InteropServices.Marshal.StringToHGlobalAnsi(path).ToPointer();
            //char* valueCh = (char*)System.Runtime.InteropServices.Marshal.StringToHGlobalAnsi(value).ToPointer();
            return 1 == property_.SetValue(path, value);
        }

        private void AddData(ZCAN_Receive_Data[] data, uint len)
        {
            list_box_data_ = "";
            for (uint i = 0; i < len; ++i)
            {
                ZCAN_Receive_Data can = data[i];
                uint id = data[i].frame.can_id;
                string eff = IsEFF(id) ? "扩展帧" : "标准帧";
                string rtr = IsRTR(id) ? "远程帧" : "数据帧";
                list_box_data_ = String.Format("接收到CAN ID:0x{0:X8} {1:G} {2:G} 长度:{3:D1} 数据:", GetId(id), eff, rtr, can.frame.can_dlc);
                int index = this.dataGridView1.Rows.Add();
                dataGridView1.Rows[index].Cells[0].Value = i;
                dataGridView1.Rows[index].Cells[1].Value = "0x" + GetId(id);
                dataGridView1.Rows[index].Cells[2].Value = "接收";
                dataGridView1.Rows[index].Cells[3].Value = eff;
                dataGridView1.Rows[index].Cells[4].Value = can.frame.can_dlc;
                for (uint j = 0; j < can.frame.can_dlc; ++j)
                {
                    list_box_data_ = String.Format("{0:G}{1:X2} ", list_box_data_, can.frame.data[j]);
                }
                dataGridView1.Rows[index].Cells[5].Value = list_box_data_;
            }

            Object[] list = { this, System.EventArgs.Empty };
            this.listBox.BeginInvoke(new EventHandler(SetListBox), list);
        }
        private void SetListBox(object sender, EventArgs e)
        {
            int index = listBox.Items.Add(list_box_data_);
            listBox.SelectedIndex = index;
        }
        private void AddData(ZCAN_ReceiveFD_Data[] data, uint len)
        {
            list_box_data_ = "";
            for (uint i = 0; i < len; ++i)
            {
                ZCAN_ReceiveFD_Data canfd = data[i];
                uint id = data[i].frame.can_id;
                string eff = IsEFF(id) ? "扩展帧" : "标准帧";
                string rtr = IsRTR(id) ? "远程帧" : "数据帧";
                list_box_data_ = String.Format("接收到CANFD ID:0x{0:X8} {1:G} {2:G} 长度:{3:D1} 数据:", GetId(id), eff, rtr, canfd.frame.len);
                for (uint j = 0; j < canfd.frame.len; ++j)
                {
                    list_box_data_ = String.Format("{0:G}{1:X2} ", list_box_data_, canfd.frame.data[j]);
                }
            }

            Object[] list = { this, System.EventArgs.Empty };
            this.listBox.BeginInvoke(new EventHandler(SetListBox), list);
        }

        private void AddErr()
        {
            ZCAN_CHANNEL_ERROR_INFO pErrInfo = new ZCAN_CHANNEL_ERROR_INFO();
            IntPtr ptr = Marshal.AllocHGlobal(Marshal.SizeOf(pErrInfo));
            Marshal.StructureToPtr(pErrInfo, ptr, true);
            if (Method.ZCAN_ReadChannelErrInfo(channel_handle_, ptr) != Define.STATUS_OK)
            {
                MessageBox.Show("获取错误信息失败", "提示", MessageBoxButtons.OK, MessageBoxIcon.Exclamation);
            }
            Marshal.FreeHGlobal(ptr);

            string errorInfo = String.Format("错误码：{0:D1}", pErrInfo.error_code);
            int index = listBox.Items.Add(errorInfo);
            listBox.SelectedIndex = index;
        }

        //
        private void Start_CAN_Click(object sender, EventArgs e)
        {
            if (Method.ZCAN_StartCAN(channel_handle_) != Define.STATUS_OK)
            {
                MessageBox.Show("启动CAN失败", "提示", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            Start_CAN.Enabled = false;
            m_bStart = true;
            if (null == recv_data_thread_)
            {
                recv_data_thread_ = new recvdatathread();
                recv_data_thread_.setChannelHandle(channel_handle_);
                recv_data_thread_.setStart(m_bStart);
                recv_data_thread_.RecvCANData += this.AddData;
                recv_data_thread_.RecvFDData += this.AddData;
            }
            else
            {
                recv_data_thread_.setChannelHandle(channel_handle_);
            }
        }

        //判断数据发送框是否为空，为空则失能发送键
        private void data_trans_TextChanged(object sender, EventArgs e)
        {
            if (data_trans.Text.Trim() == String.Empty)
            {
                transmite.Enabled = false;
            }
            else
                transmite.Enabled = true;
        }
        private void label1_Click(object sender, EventArgs e)
        {

        }

        private void statusStrip1_ItemClicked(object sender, ToolStripItemClickedEventArgs e)
        {

        }

        private void comboBox1_SelectedIndexChanged(object sender, EventArgs e)
        {

        }

        private void label4_Click(object sender, EventArgs e)
        {

        }

        private void comboBox4_SelectedIndexChanged(object sender, EventArgs e)
        {

        }

        private void label3_Click(object sender, EventArgs e)
        {

        }

        private void comboBox3_SelectedIndexChanged(object sender, EventArgs e)
        {

        }

        private void label5_Click(object sender, EventArgs e)
        {

        }

        private void label8_Click(object sender, EventArgs e)
        {

        }

        private void dataGridView1_CellContentClick(object sender, DataGridViewCellEventArgs e)
        {

        }

        private void comboBox5_SelectedIndexChanged(object sender, EventArgs e)
        {

        }

        private void data_trans_Click(object sender, EventArgs e)
        {

        }

        private void label6_Click(object sender, EventArgs e)
        {

        }

        private void progressBar1_Click(object sender, EventArgs e)
        {

        }

        private void label16_Click(object sender, EventArgs e)
        {

        }

        private void openFileDialog1_FileOk(object sender, CancelEventArgs e)
        {
            
        }

        private void Loadfile_Click(object sender, EventArgs e)
        {
            OpenFileDialog ofd = new OpenFileDialog();
            ofd.Filter = "Hex Files|*.hex|S19 Files|*.s19|bin Files|*.bin|所有文件|*.*";
            ofd.Title = "选择文件路径";
            if(ofd.ShowDialog() == DialogResult.OK)
            {
                fName = ofd.FileName;
                Files.Text = fName;
            }
            
        }

        private void Files_Click(object sender, EventArgs e)
        {

        }
       
        
        private void richTextBox1_TextChanged_1(object sender, EventArgs e)
        {

        }

        private void listBox1_SelectedIndexChanged(object sender, EventArgs e)
        {

        }

        //拆分text到发送data数组
        private int SplitData(string data, ref byte[] transData, int maxLen)
        {
            string[] dataArray = data.Split(' ');
            for (int i = 0; (i < maxLen) && (i < dataArray.Length); i++)
            {
                transData[i] = Convert.ToByte(dataArray[i].Substring(0, 2), 16);
            }
            return dataArray.Length;
        }

        private void transmite_Click(object sender, EventArgs e)
        {
            if (data_trans.Text.Length == 0)
            {
                MessageBox.Show("数据为空", "提示", MessageBoxButtons.OK, MessageBoxIcon.Exclamation);
                return;
            }
            uint id = (uint)System.Convert.ToInt32(Frame_ID.Text, 16);
            string data = data_trans.Text;
            int frame_type_index = 0;   //0为标准帧，1为拓展帧
            int protocol_index = CAN_type.SelectedIndex;
            int send_type_index = Trans_Mode.SelectedIndex;
            int canfd_exp_index = 0;
            uint result; //发送的帧数

            if (0 == protocol_index) //can
            {
                ZCAN_Transmit_Data can_data = new ZCAN_Transmit_Data();
                can_data.frame.can_id = MakeCanId(id, frame_type_index, 0, 0);
                can_data.frame.data = new byte[8];
                can_data.frame.can_dlc = (byte)SplitData(data, ref can_data.frame.data, CAN_MAX_DLEN);
                can_data.transmit_type = (uint)send_type_index;
                IntPtr ptr = Marshal.AllocHGlobal(Marshal.SizeOf(can_data));
                Marshal.StructureToPtr(can_data, ptr, true);
                result = Method.ZCAN_Transmit(channel_handle_, ptr, 1);
                Marshal.FreeHGlobal(ptr);
            }
            else //canfd
            {
                ZCAN_TransmitFD_Data canfd_data = new ZCAN_TransmitFD_Data();
                canfd_data.frame.can_id = MakeCanId(id, frame_type_index, 0, 0);
                canfd_data.frame.data = new byte[64];
                canfd_data.frame.len = (byte)SplitData(data, ref canfd_data.frame.data, CANFD_MAX_DLEN);
                canfd_data.transmit_type = (uint)send_type_index;
                canfd_data.frame.flags = (byte)((canfd_exp_index != 0) ? CANFD_BRS : 0);
                IntPtr ptr = Marshal.AllocHGlobal(Marshal.SizeOf(canfd_data));
                Marshal.StructureToPtr(canfd_data, ptr, true);
                result = Method.ZCAN_TransmitFD(channel_handle_, ptr, 1);
                Marshal.FreeHGlobal(ptr);
            }

            if (result != 1)
            {
                MessageBox.Show("发送数据失败", "提示", MessageBoxButtons.OK, MessageBoxIcon.Exclamation);
                AddErr();
            }
        }

        private void Clear_Click(object sender, EventArgs e)
        {
            listBox.Items.Clear();
        }

        private void tabPage2_Click(object sender, EventArgs e)
        {

        }

        private void serialnumBindingSource1_CurrentChanged(object sender, EventArgs e)
        {

        }

        private void COM_Num_SelectedIndexChanged(object sender, EventArgs e)
        {

        }

        private void tabControl1_SelectedIndexChanged(object sender, EventArgs e)
        {

        }

        private void label17_Click(object sender, EventArgs e)
        {

        }

        private void CAN_type_SelectedIndexChanged(object sender, EventArgs e)
        {

        }

        private void label23_Click(object sender, EventArgs e)
        {

        }

        private void Firmware_Num_Click(object sender, EventArgs e)
        {

        }

        private void Trans_Mode_SelectedIndexChanged(object sender, EventArgs e)
        {

        }

        private void label15_Click(object sender, EventArgs e)
        {

        }

        private void Frame_ID_TextChanged(object sender, EventArgs e)
        {

        }

        private void label21_Click(object sender, EventArgs e)
        {

        }

        private void listBox2_SelectedIndexChanged(object sender, EventArgs e)
        {

        }

        private void trans_long_SelectedIndexChanged(object sender, EventArgs e)
        {

        }

        private void Version_Num_Click(object sender, EventArgs e)
        {

        }

        private void label19_Click(object sender, EventArgs e)
        {

        }

        private void label3_Click_1(object sender, EventArgs e)
        {

        }

        private void Device_index_SelectedIndexChanged(object sender, EventArgs e)
        {

        }

        private void Driver_Num_Click(object sender, EventArgs e)
        {

        }

        private void Baundrate_SelectedIndexChanged(object sender, EventArgs e)
        {

        }

        private void label5_Click_1(object sender, EventArgs e)
        {

        }

        private void label8_Click_1(object sender, EventArgs e)
        {

        }

        private void Mode_Click(object sender, EventArgs e)
        {

        }

        private void label14_Click(object sender, EventArgs e)
        {

        }

        private void Disconnect_Click(object sender, EventArgs e)
        {

        }

        private void ECU_reset_Click(object sender, EventArgs e)
        {

        }

        private void Download_Click(object sender, EventArgs e)
        {

        }

        private void label2_Click(object sender, EventArgs e)
        {

        }

        private void directionBindingSource_CurrentChanged(object sender, EventArgs e)
        {

        }

        private void Device_SerialNum_Click(object sender, EventArgs e)
        {

        }

        private void tabPage1_Click(object sender, EventArgs e)
        {

        }

        private void Device_type_SelectedIndexChanged(object sender, EventArgs e)
        {

        }

        private void frameidBindingSource_CurrentChanged(object sender, EventArgs e)
        {

        }

        private void label12_Click(object sender, EventArgs e)
        {

        }

        private void Working_Mode_SelectedIndexChanged(object sender, EventArgs e)
        {

        }

        private void label20_Click(object sender, EventArgs e)
        {

        }

        private void label1_Click_1(object sender, EventArgs e)
        {

        }

        private void label4_Click_1(object sender, EventArgs e)
        {

        }

        private void label10_Click(object sender, EventArgs e)
        {

        }

        private void serialnumBindingSource_CurrentChanged(object sender, EventArgs e)
        {

        }

        private void Hardware_type_Click(object sender, EventArgs e)
        {

        }

        private void dataGridView1_CellContentClick_1(object sender, DataGridViewCellEventArgs e)
        {

        }

        private void dataGridView2_CellContentClick(object sender, DataGridViewCellEventArgs e)
        {

        }

        private void tabControl1_SelectedIndexChanged_1(object sender, EventArgs e)
        {

        }

        private void serialnumBindingSource1_CurrentChanged_1(object sender, EventArgs e)
        {

        }

        private void COM_Num_SelectedIndexChanged_1(object sender, EventArgs e)
        {

        }

        private void label17_Click_1(object sender, EventArgs e)
        {

        }

        private void CAN_type_SelectedIndexChanged_1(object sender, EventArgs e)
        {

        }

        private void label23_Click_1(object sender, EventArgs e)
        {

        }

        private void Firmware_Num_Click_1(object sender, EventArgs e)
        {

        }

        private void Trans_Mode_SelectedIndexChanged_1(object sender, EventArgs e)
        {

        }

        private void label15_Click_1(object sender, EventArgs e)
        {

        }

        private void Frame_ID_TextChanged_1(object sender, EventArgs e)
        {

        }

        private void label21_Click_1(object sender, EventArgs e)
        {

        }

        private void listBox2_SelectedIndexChanged_1(object sender, EventArgs e)
        {

        }

        private void Device_index_SelectedIndexChanged_1(object sender, EventArgs e)
        {

        }

        private void trans_long_SelectedIndexChanged_1(object sender, EventArgs e)
        {

        }

        private void Version_Num_Click_1(object sender, EventArgs e)
        {

        }

        private void label19_Click_1(object sender, EventArgs e)
        {

        }

        private void label3_Click_2(object sender, EventArgs e)
        {

        }

        private void Driver_Num_Click_1(object sender, EventArgs e)
        {

        }

        private void Baundrate_SelectedIndexChanged_1(object sender, EventArgs e)
        {

        }

        private void label5_Click_2(object sender, EventArgs e)
        {

        }

        private void label8_Click_2(object sender, EventArgs e)
        {

        }

        private void Mode_Click_1(object sender, EventArgs e)
        {

        }

        private void label14_Click_1(object sender, EventArgs e)
        {

        }

        private void Disconnect_Click_1(object sender, EventArgs e)
        {

        }

        private void tabPage2_Click_1(object sender, EventArgs e)
        {

        }

        private void ECU_reset_Click_1(object sender, EventArgs e)
        {

        }

        private void Download_Click_1(object sender, EventArgs e)
        {

        }

        private void label2_Click_1(object sender, EventArgs e)
        {

        }

        private void directionBindingSource_CurrentChanged_1(object sender, EventArgs e)
        {

        }

        private void Device_SerialNum_Click_1(object sender, EventArgs e)
        {

        }

        private void tabPage1_Click_1(object sender, EventArgs e)
        {

        }

        private void Device_type_SelectedIndexChanged_1(object sender, EventArgs e)
        {

        }

        private void frameidBindingSource_CurrentChanged_1(object sender, EventArgs e)
        {

        }

        private void serialnumBindingSource_CurrentChanged_1(object sender, EventArgs e)
        {

        }

        private void Hardware_type_Click_1(object sender, EventArgs e)
        {

        }

        private void Working_Mode_SelectedIndexChanged_1(object sender, EventArgs e)
        {

        }

        private void dataGridView2_CellContentClick_1(object sender, DataGridViewCellEventArgs e)
        {

        }

        private void skinSplitContainer1_Panel1_Paint(object sender, PaintEventArgs e)
        {

        }

        private void skinTabControl1_SelectedIndexChanged(object sender, EventArgs e)
        {

        }

        private void toolStripButton1_Click(object sender, EventArgs e)
        {

        }

        private void splitContainer1_Panel1_Paint(object sender, PaintEventArgs e)
        {

        }

        private void data_trans_TextChanged_1(object sender, EventArgs e)
        {
            if (data_trans.Text.Trim() == String.Empty)
            {
                transmite.Enabled = false;
            }
            else
                transmite.Enabled = true;
        }

        private void groupBox2_Enter(object sender, EventArgs e)
        {

        }

        private void skinButton1_Click(object sender, EventArgs e)
        {
            Description.Text = "诊断会话控制";
            Service_ID.Text = "10";
            ArrayList list = new ArrayList();
            list.Add(new DictionaryEntry("0", "01 默认会话"));
            list.Add(new DictionaryEntry("1", "02 编程会话"));
            list.Add(new DictionaryEntry("2", "03 拓展诊断会话"));
            list.Add(new DictionaryEntry("3", "04 安全系统诊断会话"));
            Subserver.DataSource = list;
            Subserver.DisplayMember = "Value";
            Subserver.ValueMember = "Key";
            Subserver.Enabled = true;
            response_reject.Enabled = true;
        }

        private void skinButton2_Click(object sender, EventArgs e)
        {
            Description.Text = "ECU重置";
            Service_ID.Text = "11";
            ArrayList list = new ArrayList();
            list.Add(new DictionaryEntry("0", "01 硬重置"));
            list.Add(new DictionaryEntry("1", "02 点火钥匙关闭/重置"));
            list.Add(new DictionaryEntry("2", "03 软重置"));
            list.Add(new DictionaryEntry("3", "04 启动快速断电"));
            list.Add(new DictionaryEntry("4", "05 禁用快速断电"));
            Subserver.DataSource = list;
            Subserver.DisplayMember = "Value";
            Subserver.ValueMember = "Key";
            Subserver.Enabled = true;
            response_reject.Enabled = true;
        }

        private void skinButton3_Click(object sender, EventArgs e)
        {
            Description.Text = "清除诊断信息";
            Service_ID.Text = "14";
            Subserver.Enabled = false;
            response_reject.Enabled = false;
        }

        private void Add_to_List_Click(object sender, EventArgs e)
        {
            uint Subserver_select = (uint)Subserver.SelectedIndex;
            uint response = (uint)response_reject.SelectedIndex;
            if (response == 0) response = 0;
            else response = 8;
            int index = this.dataGridView3.Rows.Add();
            dataGridView3.Rows[index].Cells[0].Value = serial++;
            dataGridView3.Rows[index].Cells[1].Value = Description.Text;
            if(Convert.ToInt32(Service_ID.Text) == 14)
                dataGridView3.Rows[index].Cells[2].Value = String.Format("{0} {1}", Service_ID.Text,  textBox2.Text);
            else
                dataGridView3.Rows[index].Cells[2].Value = String.Format("{0} {1}{2} {3}", Service_ID.Text , response, Subserver_select + 1, textBox2.Text);
        }
    }
}
