using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Net;
using System.Net.Sockets;
using System.IO;
namespace file_transfer
{
    public delegate void TransferEventHandler(object sender, TransferQueue queue);
    public delegate void ConnectCallback(object sender, string error);
    public class TransferClient
    {
        // Biến lưu socket đã kết nối hoặc đang kết nối.
        private Socket _baseSocket;
        // Bộ đệm nhận dữ liệu.
        private byte[] _buffer = new byte[8192];
        // Biến dùng để xử lý callback khi kết nối.
        private ConnectCallback _connectCallback;
        // Danh sách lưu trữ tất cả các hàng đợi truyền tải (bao gồm tải xuống và tải lên).
        private Dictionary<int, TransferQueue> _transfers = new Dictionary<int, TransferQueue>();
        // Thuộc tính để truy cập danh sách hàng đợi truyền tải.
        public Dictionary<int, TransferQueue> Transfers
        {
            get { return _transfers; }
        }
        // Biến cho biết socket đã đóng hay chưa.
        public bool Closed
        {
            get;
            private set;
        }
        // Thư mục mặc định để lưu trữ các tệp được tải xuống.
        public string OutputFolder
        {
            get;
            set;
        }
        // Địa chỉ IP và cổng của socket đã kết nối.
        public IPEndPoint EndPoint
        {
            get;
            private set;
        }
        // Các sự kiện liên quan đến tiến trình truyền tải.
        public event TransferEventHandler Queued; // Được gọi khi một hàng đợi truyền tải được thêm vào.
        public event TransferEventHandler ProgressChanged; // Được gọi khi có tiến trình truyền tải.
        public event TransferEventHandler Stopped; // Được gọi khi một hàng đợi truyền tải dừng lại.
        public event TransferEventHandler Complete; // Được gọi khi hoàn tất một hàng đợi truyền tải.
        public event EventHandler Disconnected; // Được gọi khi kết nối bị ngắt.
        // Constructor để khởi tạo một đối tượng TransferClient và chuẩn bị kết nối.
        public TransferClient()
        {
            _baseSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        }
        // Constructor được gọi khi socket kết nối đã được listener chấp nhận.
        public TransferClient(Socket sock)
        {
            _baseSocket = sock;
            EndPoint = (IPEndPoint)_baseSocket.RemoteEndPoint; // Lấy thông tin endpoint.
        }
        // Phương thức để kết nối đến một host và cổng cụ thể.
        public void Connect(string hostName, int port, ConnectCallback callback)
        {
            _connectCallback = callback; // Lưu callback vào biến cục bộ.
            _baseSocket.BeginConnect(hostName, port, connectCallback, null); // Bắt đầu kết nối bất đồng bộ.
        }
        // Callback khi kết nối hoàn tất.
        private void connectCallback(IAsyncResult ar)
        {
            string error = null;
            try
            {
                _baseSocket.EndConnect(ar); // Kết thúc quá trình kết nối.
                EndPoint = (IPEndPoint)_baseSocket.RemoteEndPoint; // Lấy thông tin endpoint.
            }
            catch (Exception ex)
            {
                error = ex.Message; // Lưu lỗi nếu xảy ra ngoại lệ.
            }
            _connectCallback(this, error); // Gọi callback với kết quả.
        }
        // Phương thức để bắt đầu nhận dữ liệu từ socket.
        public void Run()
        {
            try
            {
                _baseSocket.BeginReceive(_buffer, 0, _buffer.Length, SocketFlags.Peek, receiveCallback, null);
            }
            catch
            {
                Close(); // Đóng socket nếu xảy ra lỗi.
            }
        }
        // Thêm một hàng đợi truyền tải vào danh sách.
        public void QueueTransfer(string fileName)
        {
            try
            {
                TransferQueue queue = TransferQueue.CreateUploadQueue(this, fileName); // Tạo hàng đợi tải lên.
                _transfers.Add(queue.ID, queue); // Thêm vào danh sách.
                // Tạo và gửi gói dữ liệu hàng đợi.
                PacketWriter pw = new PacketWriter();
                pw.Write((byte)Headers.Queue);
                pw.Write(queue.ID);
                pw.Write(queue.Filename);
                pw.Write(queue.Length);
                Send(pw.GetBytes());
                // Kích hoạt sự kiện Queued.
                Queued?.Invoke(this, queue);
            }
            catch
            {
                // Bỏ qua lỗi.
            }
        }
        // Bắt đầu truyền tải một hàng đợi.
        public void StartTransfer(TransferQueue queue)
        {
            PacketWriter pw = new PacketWriter();
            pw.Write((byte)Headers.Start);
            pw.Write(queue.ID);
            Send(pw.GetBytes());
        }
        // Dừng truyền tải một hàng đợi.
        public void StopTransfer(TransferQueue queue)
        {
            if (queue.Type == QueueType.Upload)
            {
                queue.Stop(); // Dừng hàng đợi tải lên.
            }
            PacketWriter pw = new PacketWriter();
            pw.Write((byte)Headers.Stop);
            pw.Write(queue.ID);
            Send(pw.GetBytes());
            queue.Close(); // Đóng hàng đợi.
        }
        // Tạm dừng truyền tải một Queue.
        public void PauseTransfer(TransferQueue queue)
        {
            queue.Pause(); // Tạm dừng Queue.
            PacketWriter pw = new PacketWriter();
            pw.Write((byte)Headers.Pause);
            pw.Write(queue.ID);
            Send(pw.GetBytes());
        }
        // Tính toán tiến độ tổng thể của tất cả các hàng đợi truyền tải.
        public int GetOverallProgress()
        {
            int overall = 0;
            try
            {
                foreach (KeyValuePair<int, TransferQueue> pair in _transfers)
                {
                    overall += pair.Value.Progress; // Cộng dồn tiến độ của từng hàng đợi.
                }
                if (overall > 0)
                {
                    overall = (overall * 100) / (_transfers.Count * 100); // Tính phần trăm tiến độ tổng.
                }
            }
            catch
            {
                overall = 0; // Nếu lỗi, trả về 0.
            }
            return overall;
        }
        public void Send(byte[] data)
        {
            //If our client is disposed, just return.
            if (Closed)
                return;
            //Use a lock of this instance so we can't send multiple things at a time.
            lock (this)
            {
                try
                {
                    //Send the size of the packet.
                    _baseSocket.Send(BitConverter.GetBytes(data.Length), 0, 4, SocketFlags.None);
                    //And then the actual packet.
                    _baseSocket.Send(data, 0, data.Length, SocketFlags.None);
                }
                catch
                {
                    Close();
                }
            }
        }
        public void Close()
        {
            // Nếu socket đã đóng, thoát ra để tránh xử lý lại.
            if (Closed)
                return;
            Closed = true; // Đánh dấu rằng socket đã đóng.
            _baseSocket.Close(); // Đóng socket.
            _transfers.Clear(); // Xóa tất cả các hàng đợi truyền tải.
            _transfers = null; // Giải phóng tài nguyên danh sách hàng đợi.
            _buffer = null; // Giải phóng bộ đệm.
            OutputFolder = null; // Giải phóng thư mục đầu ra.
            // Gọi sự kiện Disconnected để thông báo rằng kết nối đã bị ngắt.
            if (Disconnected != null)
                Disconnected(this, EventArgs.Empty);
        }
        private void process()
        {
            // Tạo đối tượng đọc gói tin từ bộ đệm.
            PacketReader pr = new PacketReader(_buffer);
            // Đọc và chuyển đổi tiêu đề của gói tin.
            Headers header = (Headers)pr.ReadByte();
            // Xử lý dựa trên loại tiêu đề.
            switch (header)
            {
                case Headers.Queue:
                    {
                        // Đọc thông tin ID, tên tệp và độ dài tệp từ gói tin.
                        int id = pr.ReadInt32();
                        string fileName = pr.ReadString();
                        long length = pr.ReadInt64();
                        // Tạo hàng đợi tải xuống với thông tin vừa nhận được.
                        TransferQueue queue = TransferQueue.CreateDownloadQueue(
                            this,
                            id,
                            Path.Combine(OutputFolder, Path.GetFileName(fileName)),
                            length
                        );
                        // Thêm hàng đợi vào danh sách truyền tải.
                        _transfers.Add(id, queue);
                        // Gọi sự kiện Queued nếu đã được đăng ký.
                        if (Queued != null)
                        {
                            Queued(this, queue);
                        }
                    }
                    break;

                case Headers.Start:
                    {
                        // Đọc ID từ gói tin.
                        int id = pr.ReadInt32();

                        // Bắt đầu truyền tải nếu ID có trong danh sách.
                        if (_transfers.ContainsKey(id))
                        {
                            _transfers[id].Start();
                        }
                    }
                    break;
                case Headers.Stop:
                    {
                        // Đọc ID từ gói tin.
                        int id = pr.ReadInt32();
                        if (_transfers.ContainsKey(id))
                        {
                            // Lấy hàng đợi từ danh sách.
                            TransferQueue queue = _transfers[id];
                            // Dừng và đóng hàng đợi.
                            queue.Stop();
                            queue.Close();
                            // Gọi sự kiện Stopped nếu đã được đăng ký.
                            if (Stopped != null)
                                Stopped(this, queue);
                            // Xóa hàng đợi khỏi danh sách.
                            _transfers.Remove(id);
                        }
                    }
                    break;

                case Headers.Pause:
                    {
                        // Đọc ID từ gói tin.
                        int id = pr.ReadInt32();

                        // Tạm dừng truyền tải nếu ID có trong danh sách.
                        if (_transfers.ContainsKey(id))
                        {
                            _transfers[id].Pause();
                        }
                    }
                    break;
                case Headers.Chunk:
                    {
                        // Đọc thông tin ID, chỉ số, kích thước và dữ liệu từ gói tin.
                        int id = pr.ReadInt32();
                        long index = pr.ReadInt64();
                        int size = pr.ReadInt32();
                        byte[] buffer = pr.ReadBytes(size);

                        // Lấy hàng đợi từ danh sách.
                        TransferQueue queue = _transfers[id];
                        // Ghi dữ liệu vừa nhận được vào hàng đợi tại vị trí chỉ số.
                        queue.Write(buffer, index);
                        // Cập nhật tiến độ của hàng đợi.
                        queue.Progress = (int)((queue.Transferred * 100) / queue.Length);
                        // Đảm bảo không gọi sự kiện ProgressChanged quá nhiều lần với cùng một giá trị tiến độ.
                        if (queue.LastProgress < queue.Progress)
                        {
                            queue.LastProgress = queue.Progress;
                            // Gọi sự kiện ProgressChanged nếu đã được đăng ký.
                            if (ProgressChanged != null)
                            {
                                ProgressChanged(this, queue);
                            }
                            // Nếu truyền tải hoàn tất, gọi sự kiện Complete.
                            if (queue.Progress == 100)
                            {
                                queue.Close();

                                if (Complete != null)
                                {
                                    Complete(this, queue);
                                }
                            }
                        }
                    }
                    break;
            }
            // Giải phóng đối tượng PacketReader.
            pr.Dispose();
        }
        private void receiveCallback(IAsyncResult ar)
        {
            try
            {
                // Gọi EndReceive để nhận số lượng byte dữ liệu có sẵn trong bộ đệm.
                int found = _baseSocket.EndReceive(ar);

                // Nếu số byte nhận được >= 4, nghĩa là đủ để đọc thông tin kích thước dữ liệu (4 byte).
                // Nếu ít hơn 4 byte, sẽ gọi lại phương thức Run để tiếp tục nhận.
                if (found >= 4)
                {
                    // Nhận 4 byte đầu tiên để đọc kích thước dữ liệu.
                    _baseSocket.Receive(_buffer, 0, 4, SocketFlags.None);
                    // Chuyển đổi 4 byte đầu tiên thành một số nguyên (kích thước dữ liệu).
                    int size = BitConverter.ToInt32(_buffer, 0);
                    // Nhận dữ liệu từ socket dựa trên kích thước đã xác định.
                    int read = _baseSocket.Receive(_buffer, 0, size, SocketFlags.None);
                    // Kiểm tra xem dữ liệu nhận được có bị phân mảnh hay không.
                    // Nếu số byte đọc được ít hơn kích thước, tiếp tục nhận cho đến khi đủ.
                    while (read < size)
                    {
                        read += _baseSocket.Receive(_buffer, read, size - read, SocketFlags.None);
                    }
                    // Gọi phương thức process để xử lý dữ liệu nhận được.
                    process();
                }
                // Gọi lại phương thức Run để tiếp tục quá trình nhận dữ liệu.
                Run();
            }
            catch
            {
                // Nếu có lỗi xảy ra trong quá trình nhận, đóng kết nối.
                Close();
            }
        }
        internal void callProgressChanged(TransferQueue queue)
        {
            // Kiểm tra xem sự kiện ProgressChanged đã được đăng ký hay chưa.
            // Nếu có, gọi sự kiện để thông báo về thay đổi tiến độ.
            if (ProgressChanged != null)
            {
                ProgressChanged(this, queue);
            }
        }
    }
}
