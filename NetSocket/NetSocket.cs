using System;
using System.Collections.Generic;
using System.Text;

using System.Net;
using System.Net.Sockets;

using System.Threading;
using System.Runtime.InteropServices;
using System.IO;
using System.Runtime.Serialization.Formatters.Binary;

/*
 * @package   NetSocket
 * @version   1.0
 * @author    Jeremy Messenger <jlmessengertech+githib@gmail.com>
 * @copyright 2011 Jeremy Messenger
 * @license   LGPL <http://www.gnu.org/licenses/lgpl.html>
 * @link      http://jlmessenger.com
 */
namespace JLM.NetSocket
{
	#region Enums
	public enum SocketState
	{
		Closed,
		Closing,
		Connected,
		Connecting,
		Listening,
	}
	#endregion

	#region Event Args
	public class NetSocketConnectedEventArgs : EventArgs
	{
		public IPAddress SourceIP;
		public NetSocketConnectedEventArgs(IPAddress ip)
		{
			this.SourceIP = ip;
		}
	}

	public class NetSocketDisconnectedEventArgs : EventArgs
	{
		public string Reason;
		public NetSocketDisconnectedEventArgs(string reason)
		{
			this.Reason = reason;
		}
	}

	public class NetSockStateChangedEventArgs : EventArgs
	{
		public SocketState NewState;
		public SocketState PrevState;
		public NetSockStateChangedEventArgs(SocketState newState, SocketState prevState)
		{
			this.NewState = newState;
			this.PrevState = prevState;
		}
	}

	public class NetSockDataArrivalEventArgs : EventArgs
	{
		public byte[] Data;
		public NetSockDataArrivalEventArgs(byte[] data)
		{
			this.Data = data;
		}
	}

	public class NetSockErrorReceivedEventArgs : EventArgs
	{
		public string Function;
		public Exception Exception;
		public NetSockErrorReceivedEventArgs(string function, Exception ex)
		{
			this.Function = function;
			this.Exception = ex;
		}
	}

	public class NetSockConnectionRequestEventArgs : EventArgs
	{
		public Socket Client;
		public NetSockConnectionRequestEventArgs(Socket client)
		{
			this.Client = client;
		}
	}
	#endregion

	#region Socket Classes
	public abstract class NetBase
	{
		#region Fields
		/// <summary>Current socket state</summary>
		protected SocketState state = SocketState.Closed;
		/// <summary>The socket object, obviously</summary>
		protected Socket socket;

		/// <summary>Keep track of when data is being sent</summary>
		protected bool isSending = false;

		/// <summary>Queue of objects to be sent out</summary>
		protected Queue<byte[]> sendBuffer = new Queue<byte[]>();

		/// <summary>Store incoming bytes to be processed</summary>
		protected byte[] byteBuffer = new byte[8192];

		/// <summary>Position of the bom header in the rxBuffer</summary>
		protected int rxHeaderIndex = -1;
		/// <summary>Expected length of the message from the bom header</summary>
		protected int rxBodyLen = -1;
		/// <summary>Buffer of received data</summary>
		protected MemoryStream rxBuffer = new MemoryStream();

		/// <summary>Beginning of message indicator</summary>
		protected ArraySegment<byte> bomBytes = new ArraySegment<byte>(new byte[] { 1, 2, 1, 255 });

		/// <summary>TCP inactivity before sending keep-alive packet (ms)</summary>
		protected uint KeepAliveInactivity = 500;
		/// <summary>Interval to send keep-alive packet if acknowledgement was not received (ms)</summary>
		protected uint KeepAliveInterval = 100;

		/// <summary>Threaded timer checks if socket is busted</summary>
		protected Timer connectionTimer;
		/// <summary>Interval for socket checks (ms)</summary>
		protected int ConnectionCheckInterval = 1000;
		#endregion

		#region Public Properties
		/// <summary>Current state of the socket</summary>
		public SocketState State { get { return this.state; } }

		/// <summary>Port the socket control is listening on.</summary>
		public int LocalPort
		{
			get
			{
				try
				{
					return ((IPEndPoint)this.socket.LocalEndPoint).Port;
				}
				catch
				{
					return -1;
				}
			}
		}

		/// <summary>IP address enumeration for local computer</summary>
		public static string[] LocalIP
		{
			get
			{
				IPHostEntry h = Dns.GetHostEntry(Dns.GetHostName());
				List<string> s = new List<string>(h.AddressList.Length);
				foreach (IPAddress i in h.AddressList)
					s.Add(i.ToString());
				return s.ToArray();
			}
		}
		#endregion

		#region Events
		/// <summary>Socket is connected</summary>
		public event EventHandler<NetSocketConnectedEventArgs> Connected;
		/// <summary>Socket connection closed</summary>
		public event EventHandler<NetSocketDisconnectedEventArgs> Disconnected;
		/// <summary>Socket state has changed</summary>
		/// <remarks>This has the ability to fire very rapidly during connection / disconnection.</remarks>
		public event EventHandler<NetSockStateChangedEventArgs> StateChanged;
		/// <summary>Recived a new object</summary>
		public event EventHandler<NetSockDataArrivalEventArgs> DataArrived;
		/// <summary>An error has occurred</summary>
		public event EventHandler<NetSockErrorReceivedEventArgs> ErrorReceived;
		#endregion

		#region Constructor
		/// <summary>Base constructor sets up buffer and timer</summary>
		public NetBase()
		{
			this.connectionTimer = new Timer(
				new TimerCallback(this.connectedTimerCallback),
				null, Timeout.Infinite, Timeout.Infinite);
		}
		#endregion

		#region Send
		/// <summary>Send data</summary>
		/// <param name="bytes">Bytes to send</param>
		public void Send(byte[] data)
		{
			try
			{
				if (data == null)
					throw new NullReferenceException("data cannot be null");
				else if (data.Length == 0)
					throw new NullReferenceException("data cannot be empty");
				else
				{
					lock (this.sendBuffer)
					{
						this.sendBuffer.Enqueue(data);
					}

					if (!this.isSending)
					{
						this.isSending = true;
						this.SendNextQueued();
					}
				}
			}
			catch (Exception ex)
			{
				this.OnErrorReceived("Send", ex);
			}
		}

		/// <summary>Send data for real</summary>
		private void SendNextQueued()
		{
			try
			{
				List<ArraySegment<byte>> send = new List<ArraySegment<byte>>(3);
				int length = 0;
				lock (this.sendBuffer)
				{
					if (this.sendBuffer.Count == 0)
					{
						this.isSending = false;
						return; // nothing more to send
					}

					byte[] data = this.sendBuffer.Dequeue();
					send.Add(this.bomBytes);
					send.Add(new ArraySegment<byte>(BitConverter.GetBytes(data.Length)));
					send.Add(new ArraySegment<byte>(data));

					length = this.bomBytes.Count + sizeof(int) + data.Length;
				}
				this.socket.BeginSend(send, SocketFlags.None, new AsyncCallback(this.SendCallback), this.socket);
			}
			catch (Exception ex)
			{
				this.OnErrorReceived("Sending", ex);
			}
		}

		/// <summary>Callback for BeginSend</summary>
		/// <param name="ar"></param>
		private void SendCallback(IAsyncResult ar)
		{
			try
			{
				Socket sock = (Socket)ar.AsyncState;
				int didSend = sock.EndSend(ar);

				if (this.socket != sock)
				{
					this.Close("Async Connect Socket mismatched");
					return;
				}

				this.SendNextQueued();
			}
			catch (ObjectDisposedException)
			{
				return;
			}
			catch (SocketException ex)
			{
				if (ex.SocketErrorCode == SocketError.ConnectionReset)
					this.Close("Remote Socket Closed");
				else
					throw;
			}
			catch (Exception ex)
			{
				this.Close("Socket Send Exception");
				this.OnErrorReceived("Socket Send", ex);
			}
		}
		#endregion

		#region Close
		/// <summary>Disconnect the socket</summary>
		/// <param name="reason"></param>
		public void Close(string reason)
		{
			try
			{
				if (this.state == SocketState.Closing || this.state == SocketState.Closed)
					return; // already closing/closed

				this.OnChangeState(SocketState.Closing);

				if (this.socket != null)
				{
					this.socket.Close();
					this.socket = null;
				}
			}
			catch (Exception ex)
			{
				this.OnErrorReceived("Close", ex);
			}

			try
			{
				if (this.rxBuffer.Length > 0)
				{
					if (this.rxHeaderIndex > -1 && this.rxBodyLen > -1)
					{
						// start of message - length of header
						int msgbytes = (int)this.rxBuffer.Length - this.rxHeaderIndex - this.bomBytes.Count - sizeof(int);
						this.OnErrorReceived("Close Buffer", new Exception("Incomplete Message (" + msgbytes.ToString() + " of " + this.rxBodyLen.ToString() + " bytes received)"));
					}
					else
					{
						this.OnErrorReceived("Close Buffer", new Exception("Unprocessed data " + this.rxBuffer.Length.ToString() + " bytes"));
					}
				}
			}
			catch (Exception ex)
			{
				this.OnErrorReceived("Close Buffer", ex);
			}

			try
			{
				lock (this.rxBuffer)
				{
					this.rxBuffer.SetLength(0);
				}
				lock (this.sendBuffer)
				{
					this.sendBuffer.Clear();
					this.isSending = false;
				}
				this.OnChangeState(SocketState.Closed);
				if (this.Disconnected != null)
					this.Disconnected(this, new NetSocketDisconnectedEventArgs(reason));
			}
			catch (Exception ex)
			{
				this.OnErrorReceived("Close Cleanup", ex);
			}
		}
		#endregion

		#region Receive
		/// <summary>Receive data asynchronously</summary>
		protected void Receive()
		{
			try
			{
				this.socket.BeginReceive(this.byteBuffer, 0, this.byteBuffer.Length, SocketFlags.None, new AsyncCallback(this.ReceiveCallback), this.socket);
			}
			catch (Exception ex)
			{
				this.OnErrorReceived("Receive", ex);
			}
		}

		/// <summary>Callback for BeginReceive</summary>
		/// <param name="ar"></param>
		private void ReceiveCallback(IAsyncResult ar)
		{
			try
			{
				Socket sock = (Socket)ar.AsyncState;
				int size = sock.EndReceive(ar);

				if (this.socket != sock)
				{
					this.Close("Async Receive Socket mismatched");
					return;
				}

				if (size < 1)
				{
					this.Close("No Bytes Received");
					return;
				}

				lock (this.rxBuffer)
				{
					// put at the end for safe writing
					this.rxBuffer.Position = this.rxBuffer.Length;
					this.rxBuffer.Write(this.byteBuffer, 0, size);

					bool more = false;
					do
					{
						// search for header if not found yet
						if (this.rxHeaderIndex < 0)
						{
							this.rxBuffer.Position = 0; // rewind to search
							this.rxHeaderIndex = this.IndexOfBytesInStream(this.rxBuffer, this.bomBytes.Array);
						}

						// have the header
						if (this.rxHeaderIndex > -1)
						{
							// read the body length from header
							if (this.rxBodyLen < 0 && this.rxBuffer.Length - this.rxHeaderIndex - this.bomBytes.Count >= 4)
							{
								this.rxBuffer.Position = this.rxHeaderIndex + this.bomBytes.Count; // start reading after bomBytes
								this.rxBuffer.Read(this.byteBuffer, 0, 4); // read message length
								this.rxBodyLen = BitConverter.ToInt32(this.byteBuffer, 0);
							}

							// we have the message
							if (this.rxBodyLen > -1 && (this.rxBuffer.Length - this.rxHeaderIndex - this.bomBytes.Count - 4) >= this.rxBodyLen)
							{
								try
								{
									this.rxBuffer.Position = this.rxHeaderIndex + this.bomBytes.Count + sizeof(int);
									byte[] data = new byte[this.rxBodyLen];
									this.rxBuffer.Read(data, 0, data.Length);
									if (this.DataArrived != null)
										this.DataArrived(this, new NetSockDataArrivalEventArgs(data));
								}
								catch (Exception ex)
								{
									this.OnErrorReceived("Receiving", ex);
								}

								if (this.rxBuffer.Position == this.rxBuffer.Length)
								{
									// no bytes left
									// just resize buffer
									this.rxBuffer.SetLength(0);
									this.rxBuffer.Capacity = this.byteBuffer.Length;
									more = false;
								}
								else
								{
									// leftover bytes after current message
									// copy these bytes to the beginning of the rxBuffer
									this.CopyBack();
									more = true;
								}

								// reset header info
								this.rxHeaderIndex = -1;
								this.rxBodyLen = -1;
							}
							else if (this.rxHeaderIndex > 0)
							{
								// remove bytes from before the header
								this.rxBuffer.Position = this.rxHeaderIndex;
								this.CopyBack();
								this.rxHeaderIndex = 0;
								more = false;
							}
							else
								more = false;
						}
					} while (more);
				}
				this.socket.BeginReceive(this.byteBuffer, 0, this.byteBuffer.Length, SocketFlags.None, new AsyncCallback(this.ReceiveCallback), this.socket);
			}
			catch (ObjectDisposedException)
			{
				return; // socket disposed, let it die quietly
			}
			catch (SocketException ex)
			{
				if (ex.SocketErrorCode == SocketError.ConnectionReset)
					this.Close("Remote Socket Closed");
				else
					throw;
			}
			catch (Exception ex)
			{
				this.Close("Socket Receive Exception");
				this.OnErrorReceived("Socket Receive", ex);
			}
		}

		/// <summary>
		/// Copies the stuff after the current position, back to the start of the stream,
		/// resizes the stream to only include the new content, and
		/// limits the capacity to length + another buffer.
		/// </summary>
		private void CopyBack()
		{
			int count;
			long readPos = this.rxBuffer.Position;
			long writePos = 0;
			do
			{
				count = this.rxBuffer.Read(this.byteBuffer, 0, this.byteBuffer.Length);
				readPos = this.rxBuffer.Position;
				this.rxBuffer.Position = writePos;
				this.rxBuffer.Write(this.byteBuffer, 0, count);
				writePos = this.rxBuffer.Position;
				this.rxBuffer.Position = readPos;
			}
			while (count > 0);
			this.rxBuffer.SetLength(writePos);
			this.rxBuffer.Capacity = (int)this.rxBuffer.Length + this.byteBuffer.Length;
		}

		/// <summary>Find first position the specified byte within the stream, or -1 if not found</summary>
		/// <param name="ms"></param>
		/// <param name="find"></param>
		/// <returns></returns>
		private int IndexOfByteInStream(MemoryStream ms, byte find)
		{
			int b;
			do
			{
				b = ms.ReadByte();
			} while(b > -1 && b != find);

			if (b == -1)
				return -1;
			else
				return (int)ms.Position - 1; // position is +1 byte after the byte we want
		}

		/// <summary>Find first position the specified bytes within the stream, or -1 if not found</summary>
		/// <param name="ms"></param>
		/// <param name="find"></param>
		/// <returns></returns>
		private int IndexOfBytesInStream(MemoryStream ms, byte[] find)
		{
			int index;
			do
			{
				index = this.IndexOfByteInStream(ms, find[0]);

				if (index > -1)
				{
					bool found = true;
					for (int i = 1; i < find.Length; i++)
					{
						if(find[i] != ms.ReadByte())
						{
							found = false;
							ms.Position = index + 1;
							break;
						}
					}
					if (found)
						return index;
				}
			} while(index > -1);
			return -1;
		}
		#endregion

		#region OnEvents
		protected void OnErrorReceived(string function, Exception ex)
		{
			if (this.ErrorReceived != null)
				this.ErrorReceived(this, new NetSockErrorReceivedEventArgs(function, ex));
		}

		protected void OnConnected(Socket sock)
		{
			if (this.Connected != null)
				this.Connected(this, new NetSocketConnectedEventArgs(((IPEndPoint)sock.RemoteEndPoint).Address));
		}

		protected void OnChangeState(SocketState newState)
		{
			SocketState prev = this.state;
			this.state = newState;
			if (this.StateChanged != null)
				this.StateChanged(this, new NetSockStateChangedEventArgs(this.state, prev));

			if (this.state == SocketState.Connected)
				this.connectionTimer.Change(0, this.ConnectionCheckInterval);
			else if (this.state == SocketState.Closed)
				this.connectionTimer.Change(Timeout.Infinite, Timeout.Infinite);
		}
		#endregion

		#region Keep-alives
		/*
		 * Note about usage of keep-alives
		 * The TCP protocol does not successfully detect "abnormal" socket disconnects at both
		 * the client and server end. These are disconnects due to a computer crash, cable 
		 * disconnect, or other failure. The keep-alive mechanism built into the TCP socket can
		 * detect these disconnects by essentially sending null data packets (header only) and
		 * waiting for acks.
		 */

		/// <summary>Structure for settings keep-alive bytes</summary>
		[StructLayout(LayoutKind.Sequential)]
		private struct tcp_keepalive
		{
			/// <summary>1 = on, 0 = off</summary>
			public uint onoff;
			/// <summary>TCP inactivity before sending keep-alive packet (ms)</summary>
			public uint keepalivetime;
			/// <summary>Interval to send keep-alive packet if acknowledgement was not received (ms)</summary>
			public uint keepaliveinterval;
		}

		/// <summary>Set up the socket to use TCP keep alive messages</summary>
		protected void SetKeepAlive()
		{
			try
			{
				tcp_keepalive sioKeepAliveVals = new tcp_keepalive();
				sioKeepAliveVals.onoff = (uint)1; // 1 to enable 0 to disable
				sioKeepAliveVals.keepalivetime = this.KeepAliveInactivity;
				sioKeepAliveVals.keepaliveinterval = this.KeepAliveInterval;

				IntPtr p = Marshal.AllocHGlobal(Marshal.SizeOf(sioKeepAliveVals));
				Marshal.StructureToPtr(sioKeepAliveVals, p, true);
				byte[] inBytes = new byte[Marshal.SizeOf(sioKeepAliveVals)];
				Marshal.Copy(p, inBytes, 0, inBytes.Length);
				Marshal.FreeHGlobal(p);

				byte[] outBytes = BitConverter.GetBytes(0);
				this.socket.IOControl(IOControlCode.KeepAliveValues, inBytes, outBytes);
			}
			catch (Exception ex)
			{
				this.OnErrorReceived("Keep Alive", ex);
			}
		}
		#endregion

		#region Connection Sanity Check
		private void connectedTimerCallback(object sender)
		{
			try
			{
				if (this.state == SocketState.Connected &&
					(this.socket == null || !this.socket.Connected))
					this.Close("Connect Timer");
			}
			catch (Exception ex)
			{
				this.OnErrorReceived("ConnectTimer", ex);
				this.Close("Connect Timer Exception");
			}
		}
		#endregion
	}

	public class NetServer : NetBase
	{
		#region Events
		/// <summary>A socket has requested a connection</summary>
		public event EventHandler<NetSockConnectionRequestEventArgs> ConnectionRequested;
		#endregion

		#region Listen
		/// <summary>Listen for incoming connections</summary>
		/// <param name="port">Port to listen on</param>
		public void Listen(int port)
		{
			try
			{
				if (this.socket != null)
				{
					try
					{
						this.socket.Close();
					}
					catch { }; // ignore problems with old socket
				}
				this.socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);

				IPEndPoint ipLocal = new IPEndPoint(IPAddress.Any, port);
				this.socket.Bind(ipLocal);
				this.socket.Listen(1);
				this.socket.BeginAccept(new AsyncCallback(this.AcceptCallback), this.socket);
				this.OnChangeState(SocketState.Listening);
			}
			catch (Exception ex)
			{
				this.OnErrorReceived("Listen", ex);
			}
		}

		/// <summary>Callback for BeginAccept</summary>
		/// <param name="ar"></param>
		private void AcceptCallback(IAsyncResult ar)
		{
			try
			{
				Socket listener = (Socket)ar.AsyncState;
				Socket sock = listener.EndAccept(ar);

				if (this.state == SocketState.Listening)
				{
					if (this.socket != listener)
					{
						this.Close("Async Listen Socket mismatched");
						return;
					}

					if (this.ConnectionRequested != null)
						this.ConnectionRequested(this, new NetSockConnectionRequestEventArgs(sock));
				}

				if (this.state == SocketState.Listening)
					this.socket.BeginAccept(new AsyncCallback(this.AcceptCallback), listener);
				else
				{
					try
					{
						listener.Close();
					}
					catch (Exception ex)
					{
						this.OnErrorReceived("Close Listen Socket", ex);
					}
				}
			}
			catch (ObjectDisposedException)
			{
				return;
			}
			catch (SocketException ex)
			{
				this.Close("Listen Socket Exception");
				this.OnErrorReceived("Listen Socket", ex);
			}
			catch (Exception ex)
			{
				this.OnErrorReceived("Listen Socket", ex);
			}
		}
		#endregion

		#region Accept
		/// <summary>Accept the connection request</summary>
		/// <param name="client">Client socket to accept</param>
		public void Accept(Socket client)
		{
			try
			{
				if (this.state != SocketState.Listening)
					throw new Exception("Cannot accept socket is " + this.state.ToString());

				if (this.socket != null)
				{
					try
					{
						this.socket.Close(); // close listening socket
					}
					catch { } // don't care if this fails
				}

				this.socket = client;

				this.socket.ReceiveBufferSize = this.byteBuffer.Length;
				this.socket.SendBufferSize = this.byteBuffer.Length;

				this.SetKeepAlive();

				this.OnChangeState(SocketState.Connected);
				this.OnConnected(this.socket);

				this.Receive();
			}
			catch (Exception ex)
			{
				this.OnErrorReceived("Accept", ex);
			}
		}
		#endregion
	}

	public class NetClient : NetBase
	{
		#region Constructor
		public NetClient() : base() { }
		#endregion

		#region Connect
		/// <summary>Connect to the computer specified by Host and Port</summary>
		public void Connect(IPEndPoint endPoint)
		{
			if (this.state == SocketState.Connected)
				return; // already connecting to something

			try
			{
				if (this.state != SocketState.Closed)
					throw new Exception("Cannot connect socket is " + this.state.ToString());

				this.OnChangeState(SocketState.Connecting);

				if (this.socket == null)
					this.socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);

				this.socket.BeginConnect(endPoint, new AsyncCallback(this.ConnectCallback), this.socket);
			}
			catch (Exception ex)
			{
				this.OnErrorReceived("Connect", ex);
				this.Close("Connect Exception");
			}
		}

		/// <summary>Callback for BeginConnect</summary>
		/// <param name="ar"></param>
		private void ConnectCallback(IAsyncResult ar)
		{
			try
			{
				Socket sock = (Socket)ar.AsyncState;
				sock.EndConnect(ar);

				if (this.socket != sock)
				{
					this.Close("Async Connect Socket mismatched");
					return;
				}

				if (this.state != SocketState.Connecting)
					throw new Exception("Cannot connect socket is " + this.state.ToString());

				this.socket.ReceiveBufferSize = this.byteBuffer.Length;
				this.socket.SendBufferSize = this.byteBuffer.Length;
				
				this.SetKeepAlive();

				this.OnChangeState(SocketState.Connected);
				this.OnConnected(this.socket);

				this.Receive();
			}
			catch (Exception ex)
			{
				this.Close("Socket Connect Exception");
				this.OnErrorReceived("Socket Connect", ex);
			}
		}
		#endregion
	}
	#endregion
}
