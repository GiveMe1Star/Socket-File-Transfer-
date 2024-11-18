using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Net.Sockets;
using System.Net;
// Định nghĩa delegate cho sự kiện khi socket được chấp nhận
internal delegate void SocketAcceptedHandler(object sender, SocketAcceptedEventArgs e);
// Lớp chứa thông tin về sự kiện socket được chấp nhận
internal class SocketAcceptedEventArgs : EventArgs
{
    // Socket đã được chấp nhận
    public Socket Accepted
    {
        get;
        private set;
    }
    // Địa chỉ IP của socket đã được chấp nhận
    public IPAddress Address
    {
        get;
        private set;
    }
    // Điểm cuối của socket đã được chấp nhận
    public IPEndPoint EndPoint
    {
        get;
        private set;
    }
    // Hàm khởi tạo, nhận vào socket và thiết lập các thuộc tính
    public SocketAcceptedEventArgs(Socket sck)
    {
        Accepted = sck;
        Address = ((IPEndPoint)sck.RemoteEndPoint).Address;
        EndPoint = (IPEndPoint)sck.RemoteEndPoint;
    }
}
// Lớp Listener để lắng nghe các kết nối socket
internal class Listener
{
    #region Variables
    // Socket cơ bản
    private Socket _socket = null;
    // Biến xác định listener có đang chạy hay không
    private bool _running = false;
    // Cổng mà listener sẽ lắng nghe
    private int _port = -1;
    #endregion
    #region Properties
    // Thuộc tính trả về socket cơ bản
    public Socket BaseSocket
    {
        get { return _socket; }
    }
    // Thuộc tính trả về trạng thái chạy của listener
    public bool Running
    {
        get { return _running; }
    }
    // Thuộc tính trả về cổng mà listener đang lắng nghe
    public int Port
    {
        get { return _port; }
    }
    #endregion
    // Sự kiện khi socket được chấp nhận
    public event SocketAcceptedHandler Accepted;
    // Hàm khởi tạo mặc định
    public Listener()
    {
    }
    // Hàm bắt đầu lắng nghe trên cổng được chỉ định
    public void Start(int port)
    {
        if (_running)
            return;
        _port = port;
        _running = true;
        _socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        _socket.Bind(new IPEndPoint(IPAddress.Any, port));
        _socket.Listen(100);
        _socket.BeginAccept(acceptCallback, null);
    }
    // Hàm dừng lắng nghe
    public void Stop()
    {
        if (!_running)
            return;

        _running = false;
        _socket.Close();
    }
    // Hàm callback khi socket được chấp nhận
    private void acceptCallback(IAsyncResult ar)
    {
        try
        {
            Socket sck = _socket.EndAccept(ar);

            if (Accepted != null)
            {
                // Kích hoạt sự kiện Accepted khi socket được chấp nhận
                Accepted(this, new SocketAcceptedEventArgs(sck));
            }
        }
        catch
        {
        }
        // Nếu listener vẫn đang chạy, tiếp tục chấp nhận kết nối mới
        if (_running)
            _socket.BeginAccept(acceptCallback, null);
    }
}
