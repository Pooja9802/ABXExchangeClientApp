using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json; // Ensure you have the Newtonsoft.Json NuGet package installed

namespace AbxExchangeClient
{
    public class Packet
    {
        public string Symbol { get; set; }
        public char BuySell { get; set; }
        public int Quantity { get; set; }
        public int Price { get; set; }
        public int Sequence { get; set; }
    }

    class Program
    {
        private const int PacketSize = 17; // Packet size based on the data structure

        static async Task Main(string[] args)
        {
            string serverAddress = "localhost"; // Replace with your server address
            int port = 3000; // Replace with your server port

            try
            {
                using (var client = new TcpClient(serverAddress, port))
                using (var stream = client.GetStream())
                {
                    // Send request to stream all packets
                    await SendRequest(stream, 1);

                    var packets = new List<Packet>();
                    var receivedSequences = new HashSet<int>();

                    while (true)
                    {
                        var packet = ReceivePacket(stream);
                        if (packet == null)
                        {
                            // Server closed the connection
                            break;
                        }

                        receivedSequences.Add(packet.Sequence);
                        packets.Add(packet);
                    }

                    // Handle missing sequences
                    var maxSequence = receivedSequences.Count > 0 ? receivedSequences.Max() : 0;
                    for (int i = 1; i <= maxSequence; i++)
                    {
                        if (!receivedSequences.Contains(i))
                        {
                            await SendRequest(stream, 2, (byte?)i);
                            var missedPacket = ReceivePacket(stream);
                            if (missedPacket != null)
                            {
                                packets.Add(missedPacket);
                            }
                        }
                    }

                    // Write to JSON file
                    var json = JsonConvert.SerializeObject(new { packetStream = packets }, Formatting.Indented);
                    await File.WriteAllTextAsync("packets.json", json);

                    Console.WriteLine("Data saved to packets.json");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"An error occurred: {ex.Message}");
            }
        }

        static async Task SendRequest(NetworkStream stream, byte callType, byte? resendSeq = null)
        {
            var request = new List<byte> { callType }; // Use a List<byte> for dynamic length

            if (resendSeq.HasValue)
            {
                request.Add(resendSeq.Value);
            }

            var requestBytes = request.ToArray(); // Convert to byte array


            await stream.WriteAsync(requestBytes, 0, requestBytes.Length);
            await stream.FlushAsync();
        }

        static Packet ReceivePacket(NetworkStream stream)
        {
            var buffer = new byte[PacketSize];
            int bytesRead = 0;

            while (bytesRead < PacketSize)
            {
                int read = stream.Read(buffer, bytesRead, PacketSize - bytesRead);
                if (read == 0)
                {
                    // Connection closed or no data available
                   // return null;
                }
                bytesRead += read;
            }

            if (bytesRead == PacketSize)
            {
                try
                {
                    return new Packet
                    {
                        Symbol = Encoding.ASCII.GetString(buffer, 0, 4).Trim(),
                        BuySell = (char)buffer[4],
                        Quantity = ToInt32BigEndian(buffer, 5),
                        Price = ToInt32BigEndian(buffer, 9),
                        Sequence = ToInt32BigEndian(buffer, 13)
                    };
                }
                catch (Exception ex)
                {
                    // Log any exceptions encountered during packet processing
                    Console.WriteLine($"Error processing packet: {ex.Message}");
                    return null;
                }
            }

            return null;
        }

        static int ToInt32BigEndian(byte[] bytes, int startIndex)
        {
            if (bytes.Length < startIndex + 4)
            {
                throw new ArgumentException("Source array is not long enough.");
            }

            var bigEndianBytes = new byte[4];
            Array.Copy(bytes, startIndex, bigEndianBytes, 0, 4);
            Array.Reverse(bigEndianBytes); // Convert from big-endian to little-endian
            return BitConverter.ToInt32(bigEndianBytes, 0);
        }
    }
}
