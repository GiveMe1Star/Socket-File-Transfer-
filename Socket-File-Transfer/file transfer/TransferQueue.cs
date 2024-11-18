using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;

namespace file_transfer
{
    public enum QueueType : byte
    {
        Download,
        Upload
    }

    public class TransferQueue
    {
        public static TransferQueue CreateUploadQueue(TransferClient client, string fileName)
        {
            try
            {
                //We will create a new upload queue
                var queue = new TransferQueue();
                //Đặt tên tệp của chúng ta
                queue.Filename = Path.GetFileName(fileName);
                //Set our client
                queue.Client = client;
                //Đặt loại hàng đợi của chúng ta thành tải lên.
                queue.Type = QueueType.Upload;
                //Tạo luồng tệp của chúng ta để đọc.
                queue.FS = new FileStream(fileName, FileMode.Open);
                //Tạo luồng truyền của chúng ta
                queue.Thread = new Thread(new ParameterizedThreadStart(transferProc));
                queue.Thread.IsBackground = true;
                //Tạo ID của chúng ta
                queue.ID = Program.Rand.Next();
                //Đặt độ dài của chúng ta thành kích thước của tệp.
                queue.Length = queue.FS.Length;
                return queue;
            }
            catch
            {
                //If something goes wrong, return null
                return null;
            }
        }

        //Chúng ta sẽ tạo một hàng đợi tải xuống mới
        public static TransferQueue CreateDownloadQueue(TransferClient client, int id, string saveName, long length)
        {
            try
            {
                //Tương tự như trên với một số thay đổi.
                var queue = new TransferQueue();
                queue.Filename = Path.GetFileName(saveName);
                //Đặt client của chúng ta
                queue.Client = client;
                queue.Type = QueueType.Download;
                //Tạo luồng tệp của chúng ta để ghi.
                queue.FS = new FileStream(saveName, FileMode.Create);
                //Điền vào luồng với các byte 0 dựa trên kích thước thực. Vì vậy, chúng ta có thể ghi chỉ mục.
                queue.FS.SetLength(length);
                queue.Length = length;
                //Thay vì tạo ID, chúng ta sẽ đặt ID đã được gửi.
                queue.ID = id;
                return queue;
            }
            catch
            {
                return null;
            }
        }

        //Đây sẽ là kích thước của bộ đệm đọc của chúng ta.
        private const int FILE_BUFFER_SIZE = 8175;
        //Đây sẽ là bộ đệm đọc duy nhất mà mỗi hàng đợi truyền sẽ sử dụng để tiết kiệm bộ nhớ.
        private static byte[] file_buffer = new byte[FILE_BUFFER_SIZE];
        //Điều này sẽ được sử dụng để tạm dừng tải lên.
        private ManualResetEvent pauseEvent;
        //Đây sẽ là ID được tạo cho mỗi lần truyền.
        public int ID;
        //Điều này sẽ giữ tiến trình và tiến trình cuối cùng (để kiểm tra) cho các hàng đợi.
        public int Progress, LastProgress;
        //Những điều này sẽ giữ các byte đã truyền của chúng ta, chỉ mục đọc/ghi hiện tại và kích thước của tệp.
        public long Transferred;
        public long Index;
        public long Length;
        
public bool Running;
        public bool Paused;

        //Điều này giữ tên tệp để đọc/ghi.
        public string Filename;

        public QueueType Type;
        //Điều này sẽ giữ client truyền của chúng ta
        public TransferClient Client;
        //Điều này sẽ giữ luồng tải lên của chúng ta.
        public Thread Thread;
        //Điều này sẽ giữ luồng tệp của chúng ta để đọc/ghi.
        public FileStream FS;

        private TransferQueue()
        {
            //Khi instance được tạo, tạo một ManualResetEvent mới.
            pauseEvent = new ManualResetEvent(true);
            Running = true;
        }

        public void Start()
        {
            //Chúng ta sẽ bắt đầu luồng tải lên của mình với instance hiện tại làm tham số.
            Running = true;
            Thread.Start(this);
        }

        public void Stop()
        {
            Running = false;
        }

        public void Pause()
        {
            //Nếu nó không bị tạm dừng, đặt lại sự kiện để luồng tải lên sẽ bị chặn.
            if (!Paused)
            {
                pauseEvent.Reset();
            }
            else //Nếu nó đã bị tạm dừng, đặt sự kiện để luồng có thể tiếp tục.
            {
                pauseEvent.Set();
            }

            Paused = !Paused; //Lật biến tạm dừng.
        }

        public void Close()
        {
            try
            {
                //Xóa hàng đợi hiện tại khỏi danh sách truyền của client.
                Client.Transfers.Remove(ID);
            }
            catch { }
            Running = false;
            //Đóng luồng
            FS.Close();
            //Giải phóng ResetEvent.
            pauseEvent.Dispose();

            Client = null;
        }

        public void Write(byte[] bytes, long index)
        {
            //Khóa instance hiện tại, vì vậy chỉ một lần ghi tại một thời điểm được phép.
            lock (this)
            {
                //Đặt vị trí luồng vào chỉ mục ghi hiện tại mà chúng ta nhận được.
                FS.Position = index;
                //Ghi các byte vào luồng.
                FS.Write(bytes, 0, bytes.Length);
                //Tăng lượng dữ liệu chúng ta đã nhận
                Transferred += bytes.Length;
            }
        }

        private static void transferProc(object o)
        {
            //Chúng ta sẽ tạo một hàng đợi tải lên mới
            var queue = new TransferQueue();

            //Nếu Running là true, luồng sẽ tiếp tục
            //Nếu queue.Index không phải là độ dài tệp, luồng sẽ tiếp tục.
            while (queue.Running && queue.Index < queue.Length)
            {
                //Chúng ta sẽ gọi WaitOne để xem chúng ta có bị tạm dừng hay không.
                //Nếu chúng ta bị tạm dừng, nó sẽ chặn cho đến khi được thông báo.
                queue.pauseEvent.WaitOne();

                //Chỉ để chắc chắn rằng việc truyền đã bị tạm dừng sau đó dừng lại, kiểm tra xem chúng ta vẫn đang chạy không
                if (!queue.Running)
                {
                    break;
                }

                //Khóa bộ đệm tệp để chỉ một hàng đợi có thể sử dụng nó tại một thời điểm.
                lock (file_buffer)
                {
                    //Đặt vị trí đọc vào vị trí hiện tại của chúng ta
                    queue.FS.Position = queue.Index;

                    //Đọc một đoạn vào bộ đệm của chúng ta.
                    int read = queue.FS.Read(file_buffer, 0, file_buffer.Length);

                    //Tạo trình ghi gói của chúng ta và gửi gói đoạn của chúng ta.
                    PacketWriter pw = new PacketWriter();

                    pw.Write((byte)Headers.Chunk);
                    pw.Write(queue.ID);
                    pw.Write(queue.Index);
                    pw.Write(read);
                    pw.Write(file_buffer, 0, read);

                    /*Lý do kích thước bộ đệm là 8175 là để nó sẽ khoảng 8 kilobytes
                     * Nó nên là 8192, nhưng nó là 8191. Tôi đã bỏ lỡ một byte vì tôi phải thực hiện một thay đổi nhanh, nhưng eh.
                     * 4 Bytes = ID
                     * 8 Bytes = Index
                     * 4 Bytes = read
                     * 8175 Bytes = file_buffer
                     * Tất cả cùng nhau (Nếu bộ đệm tệp đầy) 8192 Bytes
                     * 
                     */

                    //Tăng dữ liệu đã truyền và chỉ mục đọc của chúng ta.
                    queue.Transferred += read;
                    queue.Index += read;

                    //Gửi dữ liệu của chúng ta
                    queue.Client.Send(pw.GetBytes());

                    //Lấy tiến trình của chúng ta
                    queue.Progress = (int)((queue.Transferred * 100) / queue.Length);

                    if (queue.LastProgress < queue.Progress)
                    {
                        queue.LastProgress = queue.Progress;

                        queue.Client.callProgressChanged(queue);
                    }

                    //Ngủ trong một mili giây để chúng ta không làm chết CPU của mình
                    Thread.Sleep(1);
                }
            }
            queue.Close(); //Khi vòng lặp bị phá vỡ, đóng hàng đợi.
        }
    }
}
