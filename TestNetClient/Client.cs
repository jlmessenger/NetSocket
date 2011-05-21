using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Text;
using System.Windows.Forms;

using JLM.NetSocket;
using System.IO;

namespace TestNetClient
{
	public partial class Client : Form
	{
		private NetClient client = new NetClient();

		private delegate void Safe(string n);
		private Safe SafeCall;

		public Client()
		{
			InitializeComponent();

			this.client.Connected += new EventHandler<NetSocketConnectedEventArgs>(client_Connected);
			this.client.DataArrived += new EventHandler<NetSockDataArrivalEventArgs>(client_DataArrived);
			this.client.Disconnected += new EventHandler<NetSocketDisconnectedEventArgs>(client_Disconnected);
			this.client.ErrorReceived += new EventHandler<NetSockErrorReceivedEventArgs>(client_ErrorReceived);
			this.client.StateChanged += new EventHandler<NetSockStateChangedEventArgs>(client_StateChanged);

			this.SafeCall = new Safe(Log_Local);
		}

		private void Log(string n)
		{
			if (this.InvokeRequired)
				this.Invoke(this.SafeCall, n);
			else
				this.Log_Local(n);
		}

		private void Log_Local(string n)
		{
			this.listBox1.Items.Add(n);
		}

		private void client_StateChanged(object sender, NetSockStateChangedEventArgs e)
		{
			this.Log("State: " + e.PrevState.ToString() + " -> " + e.NewState.ToString());
		}

		private void client_ErrorReceived(object sender, NetSockErrorReceivedEventArgs e)
		{
			if (e.Exception.GetType() == typeof(System.Net.Sockets.SocketException))
			{
				System.Net.Sockets.SocketException s = (System.Net.Sockets.SocketException)e.Exception;
				this.Log("Error: " + e.Function + " - " + s.SocketErrorCode.ToString() + "\r\n" + s.ToString());
			}
			else
				this.Log("Error: " + e.Function + "\r\n" + e.Exception.ToString());
		}

		private void client_Disconnected(object sender, NetSocketDisconnectedEventArgs e)
		{
			this.Log("Disconnected: " + e.Reason);
		}

		private void client_DataArrived(object sender, NetSockDataArrivalEventArgs e)
		{
			string msg;
			if (e.Data.Length > 100)
			{
				msg = "!too long!";
			}
			else
				msg = Encoding.ASCII.GetString(e.Data);

			this.Log("Recieved: " + msg + " (" + e.Data.Length.ToString() + " bytes)");
		}

		private void client_Connected(object sender, NetSocketConnectedEventArgs e)
		{
			this.Log("Connected: " + e.SourceIP);
		}

		private void buttonDisconnect_Click(object sender, EventArgs e)
		{
			this.client.Close("User forced");
		}

		private void buttonConnect_Click(object sender, EventArgs e)
		{
			System.Net.IPEndPoint end = new System.Net.IPEndPoint(System.Net.IPAddress.Parse(this.textBoxConnectTo.Text), 3333);
			this.client.Connect(end);
		}

		private void buttonSend_Click(object sender, EventArgs e)
		{
			DirectoryInfo di = new DirectoryInfo(this.textBoxSend.Text);
			foreach (FileInfo fi in di.GetFiles())
			{
				if (this.client.State != SocketState.Connected)
				{
					this.Log("Send Cancelled");
					return;
				}

				byte[] msg = File.ReadAllBytes(fi.FullName);
				byte[] name = Encoding.ASCII.GetBytes(fi.FullName);
				this.client.Send(name);
				this.client.Send(msg);
				this.Log("Sent " + fi.FullName + " (" + name.Length.ToString() + " bytes)");
				this.Log("Sent " + msg.Length.ToString() + " bytes of content");
				Application.DoEvents();
			}
			this.Log("Send Complete");
		}

		private void listBox1_SelectedIndexChanged(object sender, EventArgs e)
		{
			if (this.listBox1.SelectedItem != null)
				MessageBox.Show((string)this.listBox1.SelectedItem);
		}

		private void buttonSendText_Click(object sender, EventArgs e)
		{
			if (this.client.State != SocketState.Connected)
			{
				this.Log("Send Cancelled");
				return;
			}

			byte[] name = Encoding.ASCII.GetBytes(this.textBoxText.Text);
			if (name.Length > 0)
			{
				this.client.Send(name);
				this.Log("Sent " + this.textBoxText.Text + " (" + name.Length.ToString() + " bytes)");
			}
		}
	}
}