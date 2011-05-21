using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Text;
using System.Windows.Forms;

using JLM.NetSocket;

namespace TestNetServer
{
	public partial class Server : Form
	{
		private NetServer server = new NetServer();

		private delegate void Safe(string n);
		private Safe SafeCall;

		EventHandler<NetSockConnectionRequestEventArgs> ConnectionRequested;
		EventHandler<NetSockDataArrivalEventArgs> DataArrived;
		EventHandler<NetSocketDisconnectedEventArgs> Disconnected;

		public Server()
		{
			InitializeComponent();

			this.server.Connected += new EventHandler<NetSocketConnectedEventArgs>(server_Connected);
			this.server.ConnectionRequested += new EventHandler<NetSockConnectionRequestEventArgs>(server_ConnectionRequested);
			this.server.DataArrived += new EventHandler<NetSockDataArrivalEventArgs>(server_DataArrived);
			this.server.Disconnected += new EventHandler<NetSocketDisconnectedEventArgs>(server_Disconnected);
			this.server.ErrorReceived += new EventHandler<NetSockErrorReceivedEventArgs>(server_ErrorReceived);
			this.server.StateChanged += new EventHandler<NetSockStateChangedEventArgs>(server_StateChanged);

			ConnectionRequested = new EventHandler<NetSockConnectionRequestEventArgs>(local_ConnectionRequested);
			DataArrived = new EventHandler<NetSockDataArrivalEventArgs>(local_DataArrived);
			Disconnected = new EventHandler<NetSocketDisconnectedEventArgs>(local_Disconnected);

			this.SafeCall = new Safe(Log_Local);
			this.Load += new EventHandler(Form1_Load);
		}

		private void Form1_Load(object sender, EventArgs e)
		{
			this.server.Listen(3333);
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

		private void server_StateChanged(object sender, NetSockStateChangedEventArgs e)
		{
			this.Log("State: " + e.PrevState.ToString() + " -> " + e.NewState.ToString());
		}

		private void server_ErrorReceived(object sender, NetSockErrorReceivedEventArgs e)
		{
			if (e.Exception.GetType() == typeof(System.Net.Sockets.SocketException))
			{
				System.Net.Sockets.SocketException s = (System.Net.Sockets.SocketException)e.Exception;
				this.Log("Error: " + e.Function + " - " + s.SocketErrorCode.ToString() + "\r\n" + s.ToString());
			}
			else
				this.Log("Error: " + e.Function + "\r\n" + e.Exception.ToString());
		}

		private void server_Disconnected(object sender, NetSocketDisconnectedEventArgs e)
		{
			this.Log("Disconnected: " + e.Reason);
			this.Invoke(this.Disconnected, sender, e);
		}

		private void local_Disconnected(object sender, NetSocketDisconnectedEventArgs e)
		{
			this.server.Listen(3333);
		}

		private void server_DataArrived(object sender, NetSockDataArrivalEventArgs e)
		{
			string msg;
			if (e.Data.Length > 100)
			{
				msg = "!too long!";
			}
			else
				msg = Encoding.ASCII.GetString(e.Data);

			this.Log("Recieved: " + msg + " (" + e.Data.Length.ToString() + " bytes)");
			this.BeginInvoke(this.DataArrived, sender, e);
		}
		private void local_DataArrived(object sender, NetSockDataArrivalEventArgs e)
		{
			this.server.Send(e.Data);
		}

		private void server_ConnectionRequested(object sender, NetSockConnectionRequestEventArgs e)
		{
			this.Log("Connection Requested: " + ((System.Net.IPEndPoint)e.Client.RemoteEndPoint).Address.ToString());
			this.Invoke(this.ConnectionRequested, sender, e);
		}
		private void local_ConnectionRequested(object sender, NetSockConnectionRequestEventArgs e)
		{
			this.server.Accept(e.Client);
		}

		private void server_Connected(object sender, NetSocketConnectedEventArgs e)
		{
			this.Log("Connected: " + e.SourceIP);
		}

		private void buttonDisconnect_Click(object sender, EventArgs e)
		{
			this.server.Close("User forced");
		}

		private void listBox1_SelectedIndexChanged(object sender, EventArgs e)
		{
			if (this.listBox1.SelectedItem != null)
				MessageBox.Show((string)this.listBox1.SelectedItem);
		}
	}
}