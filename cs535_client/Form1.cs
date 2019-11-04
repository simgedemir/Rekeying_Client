using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

/*
    CS535 Homework 1
    Simge Demir
    Şevval Şimşek
     
*/

namespace cs535_client
{
    public partial class Form1 : Form
    {
        bool terminating = false;
        bool connected = false;
        Socket clientSocket;
        string seed1;
        string seed2;
        int rekeyingCount = 0;
        byte[] currentKey;
        string IP;
        int port;
        public Form1()
        {
            Control.CheckForIllegalCrossThreadCalls = false;
            this.FormClosing += new FormClosingEventHandler(Form1_FormClosing);
            InitializeComponent();
        }

        private void Form1_FormClosing(object sender, FormClosingEventArgs e)
        {
            connected = false;
            terminating = true;
            Environment.Exit(0);
        }

        private void connectButton_Click(object sender, EventArgs e)
        {
            terminating = false;
            clientSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            IP = ipBox.Text;

            if (Int32.TryParse(portBox.Text, out port))
            {
                try
                {
                    clientSocket.Connect(IP, port);
                    connectButton.Enabled = false;
                    connected = true;
                    logs.AppendText("Connected to server\n");
                    using (System.IO.StreamReader fileReader =
                        new System.IO.StreamReader("chain1.txt"))
                    {
                        seed1 = fileReader.ReadLine();
                        byte[] temp = hexStringToByteArray(seed1);
                        seed1 = Encoding.Default.GetString(temp);
                    }
                    using (System.IO.StreamReader fileReader =
                        new System.IO.StreamReader("chain2.txt"))
                    {
                        seed2 = fileReader.ReadLine();
                        byte[] temp = hexStringToByteArray(seed2);
                        seed2 = Encoding.Default.GetString(temp);
                    }
                    currentKey = getKey(1, 100); //generate the first key and store as byte array 
                    Thread receiveThread = new Thread(new ThreadStart(Receive));
                    receiveThread.Start();
                }
                catch (Exception ex)
                {
                    Console.Write(ex);
                    logs.AppendText("Could not connect to server\n");
                }
            }
            else
            {
                logs.AppendText("Check the port\n");
            }
        }

        private void Receive()
        {
            while (connected)
            {
                try
                {
                    Byte[] buffer = new Byte[512];
                    clientSocket.Receive(buffer);
                    string incomingMessage = Encoding.Default.GetString(buffer);
                    incomingMessage = incomingMessage.TrimEnd('\0');
                    int index1 = incomingMessage.IndexOf("{");
                    int index2 = incomingMessage.LastIndexOf("}");
                    string hmacStr = incomingMessage.Substring(index1 + 1, index2 - index1 - 1);
                    string encryptedMessage = incomingMessage.Substring(index2 + 1);
                    byte[] IV = new byte[16];
                    byte[] key = new byte[16];
                    Array.Copy(currentKey, 0, key, 0, 16);
                    Array.Copy(currentKey, 16, IV, 0, 16);
                    byte[] hmacsha256 = applyHMACwithSHA256(encryptedMessage, key);
                    if (hmacStr.Equals(Encoding.Default.GetString(hmacsha256)))
                    {
                        byte[] decryption = decryptWithAES128(encryptedMessage, key, IV);
                        string message = Encoding.Default.GetString(decryption);
                        logs.AppendText("Recieved message: " + message + "\n");
                        if (message.Contains("rekey"))
                        {
                            rekeyingCount++;
                            currentKey = getKey(1 + rekeyingCount, 100 - rekeyingCount);
                            logs.AppendText("Switched to new key\n");
                            //logs.AppendText("New key: "+generateHexStringFromByteArray(currentKey)+"\n");
                        }
                    }
                    else
                    {
                        logs.AppendText("HMAC cannot be verified!");
                        connectButton.Enabled = true;
                    }
                    
                }
                catch (Exception ex)
                {
                    Console.Write(ex);
                    if (!terminating)
                    {
                        logs.AppendText("The server has disconnected\n");
                        connectButton.Enabled = true;
                    }
                    clientSocket.Close();
                    connected = false;
                }
            }
        }
        public byte[] getKey(int firstIndex, int secondIndex)
        {
            string firstHash = seed1;
            string secondHash = seed2;
            for (int i = 1; i < firstIndex; i++)
            {
                byte[] result = hashWithSHA256(firstHash);
                firstHash = Encoding.Default.GetString(result);
            }
            for (int i = 1; i < secondIndex; i++)
            {
                byte[] result = hashWithSHA256(secondHash);
                secondHash = Encoding.Default.GetString(result);
            }
            byte[] key = exclusiveOR(Encoding.Default.GetBytes(firstHash), Encoding.Default.GetBytes(secondHash));
            return key;
        }
        public static byte[] exclusiveOR(byte[] arr1, byte[] arr2)
        {
            if (arr1.Length != arr2.Length)
                throw new ArgumentException("arr1 and arr2 are not the same length");

            byte[] result = new byte[arr1.Length];

            for (int i = 0; i < arr1.Length; ++i)
                result[i] = (byte)(arr1[i] ^ arr2[i]);

            return result;
        }
        static byte[] signWithRSA(string input, int algoLength, string xmlString)
        {
            // convert input string to byte array
            byte[] byteInput = Encoding.Default.GetBytes(input);
            // create RSA object from System.Security.Cryptography
            RSACryptoServiceProvider rsaObject = new RSACryptoServiceProvider(algoLength);
            // set RSA object with xml string
            rsaObject.FromXmlString(xmlString);
            byte[] result = null;

            try
            {
                result = rsaObject.SignData(byteInput, "SHA256");
            }
            catch (Exception e)
            {
                Console.WriteLine(e.Message);
            }

            return result;
        }

        // verifying with RSA
        static bool verifyWithRSA(string input, int algoLength, string xmlString, byte[] signature)
        {
            // convert input string to byte array
            byte[] byteInput = Encoding.Default.GetBytes(input);
            // create RSA object from System.Security.Cryptography
            RSACryptoServiceProvider rsaObject = new RSACryptoServiceProvider(algoLength);
            // set RSA object with xml string
            rsaObject.FromXmlString(xmlString);
            bool result = false;

            try
            {
                result = rsaObject.VerifyData(byteInput, "SHA256", signature);
            }
            catch (Exception e)
            {
                Console.WriteLine(e.Message);
            }

            return result;
        }

        // RSA encryption with varying bit length
        static byte[] encryptWithRSA(string input, int algoLength, string xmlStringKey)
        {
            // convert input string to byte array
            byte[] byteInput = Encoding.Default.GetBytes(input);
            // create RSA object from System.Security.Cryptography
            RSACryptoServiceProvider rsaObject = new RSACryptoServiceProvider(algoLength);
            // set RSA object with xml string
            rsaObject.FromXmlString(xmlStringKey);
            byte[] result = null;

            try
            {
                //true flag is set to perform direct RSA encryption using OAEP padding
                result = rsaObject.Encrypt(byteInput, true);
            }
            catch
            {
                //logs.AppendText("Encryption could not be done. \n");
            }

            return result;
        }

        public static string generateHexStringFromByteArray(byte[] input)
        {
            string hexString = BitConverter.ToString(input);
            return hexString.Replace("-", "");
        }

        public static byte[] hexStringToByteArray(string hex)
        {
            int numberChars = hex.Length;
            byte[] bytes = new byte[numberChars / 2];
            for (int i = 0; i < numberChars; i += 2)
                bytes[i / 2] = Convert.ToByte(hex.Substring(i, 2), 16);
            return bytes;
        }

        static byte[] hashWithSHA256(string input)
        {
            // convert input string to byte array
            byte[] byteInput = Encoding.Default.GetBytes(input);
            // create a hasher object from System.Security.Cryptography
            SHA256CryptoServiceProvider sha256Hasher = new SHA256CryptoServiceProvider();
            // hash and save the resulting byte array
            byte[] result = sha256Hasher.ComputeHash(byteInput);

            return result;
        }

        static byte[] applyHMACwithSHA256(string input, byte[] key)
        {
            // convert input string to byte array
            byte[] byteInput = Encoding.Default.GetBytes(input);
            // create HMAC applier object from System.Security.Cryptography
            HMACSHA256 hmacSHA256 = new HMACSHA256(key);
            // get the result of HMAC operation
            byte[] result = hmacSHA256.ComputeHash(byteInput);

            return result;
        }
        static byte[] encryptWithAES128(string input, byte[] key, byte[] IV)
        {
            // convert input string to byte array
            byte[] byteInput = Encoding.Default.GetBytes(input);

            // create AES object from System.Security.Cryptography
            RijndaelManaged aesObject = new RijndaelManaged();
            // since we want to use AES-128
            aesObject.KeySize = 128;
            // block size of AES is 128 bits
            aesObject.BlockSize = 128;
            // mode -> CipherMode.*
            aesObject.Mode = CipherMode.CFB;
            // feedback size should be equal to block size
            aesObject.FeedbackSize = 128;
            // set the key
            aesObject.Key = key;
            // set the IV
            aesObject.IV = IV;
            // create an encryptor with the settings provided
            ICryptoTransform encryptor = aesObject.CreateEncryptor();
            byte[] result = null;

            try
            {
                result = encryptor.TransformFinalBlock(byteInput, 0, byteInput.Length);
            }
            catch (Exception e) // if encryption fails
            {
                Console.WriteLine(e.Message); // display the cause
            }

            return result;
        }
        static byte[] decryptWithAES128(string input, byte[] key, byte[] IV)
        {
            // convert input string to byte array
            byte[] byteInput = Encoding.Default.GetBytes(input);

            // create AES object from System.Security.Cryptography
            RijndaelManaged aesObject = new RijndaelManaged();
            // since we want to use AES-128
            aesObject.KeySize = 128;
            // block size of AES is 128 bits
            aesObject.BlockSize = 128;
            // mode -> CipherMode.*
            aesObject.Mode = CipherMode.CFB;
            // feedback size should be equal to block size
            // aesObject.FeedbackSize = 128;
            // set the key
            aesObject.Key = key;
            // set the IV
            aesObject.IV = IV;
            // create an encryptor with the settings provided
            ICryptoTransform decryptor = aesObject.CreateDecryptor();
            byte[] result = null;

            try
            {
                result = decryptor.TransformFinalBlock(byteInput, 0, byteInput.Length);
            }
            catch (Exception e) // if encryption fails
            {
                Console.WriteLine(e.Message); // display the cause
            }

            return result;
        }

        private void sendButton_Click(object sender, EventArgs e)
        {
            String message = messageBox.Text;
            if (message.Length > 0)
            {
                
                byte[] IV = new byte[16];
                byte[] key = new byte[16];
                Array.Copy(currentKey, 0, key, 0, 16);
                Array.Copy(currentKey, 16, IV, 0, 16);
                byte[] encrypedMessage = encryptWithAES128(message, key, IV);
                byte[] hmacMessage = applyHMACwithSHA256(Encoding.Default.GetString(encrypedMessage), key);
                string newMessage = "HMAC{" + Encoding.Default.GetString(hmacMessage) + "}";
                newMessage = newMessage + Encoding.Default.GetString(encrypedMessage);
                logs.AppendText("Sent message:" + message + "\n");
                byte[] buffer = Encoding.Default.GetBytes(newMessage);
                try
                {
                    clientSocket.Send(buffer);
                }
               
                catch (Exception ex)
                {
                    Console.Write(ex);
                    if (!terminating)
                    {
                        logs.AppendText("The server has disconnected\n");
                    }

                    clientSocket.Close();
                    connected = false;
                }
                if (message.Contains("rekey"))
                {
                    rekeyingCount++;
                    currentKey = getKey(1 + rekeyingCount, 100 - rekeyingCount);
                    logs.AppendText("Switched to new key\n");
                    //logs.AppendText("New key: " + generateHexStringFromByteArray(currentKey) + "\n");
                }
            }
        }
    }
}
