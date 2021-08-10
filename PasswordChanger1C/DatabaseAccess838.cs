﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;

namespace PasswordChanger1C
{
    static class DatabaseAccess838
    {
        public static AccessFunctions.PageParams ReadInfoBase(BinaryReader reader, string TableNameUsers, int PageSize)
        {
            var bytesBlock = new byte[PageSize];

            // второй блок пропускаем
            reader.BaseStream.Seek(PageSize, SeekOrigin.Current);

            // корневой блок
            reader.Read(bytesBlock, 0, PageSize);
            var Param = FindTableDefinition(reader, bytesBlock, PageSize, TableNameUsers);
            ReadAllRecordsFromStoragePages(ref Param, reader);
            return Param;
        }

        private static AccessFunctions.PageParams FindTableDefinition(BinaryReader reader, byte[] Bytes, int PageSize, string TableUsersName)
        {
            string TargetTable = $"\"{TableUsersName.ToUpper()}\"";
            var Page = ReadObjectPageDefinition(reader, Bytes, PageSize);
            Page.BinaryData = ReadAllStoragePagesForObject(reader, Page);
            Page.PageSize = PageSize;
            int PagesCountTableStructure = Page.PagesNum.Count;
            var BytesTableStructure = Page.BinaryData;
            int[] argDataPositions = null;
            var BytesTableStructureBlockNumbers = GetCleanDataFromBlob(1, PagesCountTableStructure * PageSize, Page.BinaryData, DataPositions: ref argDataPositions);
            int TotalBlocks = BitConverter.ToInt32(BytesTableStructureBlockNumbers, 32);
            var PagesWithTableSchema = new List<int>();
            for (int j = 1; j <= TotalBlocks; j++)
            {
                int BlockNumber = BitConverter.ToInt32(BytesTableStructureBlockNumbers, 32 + j * 4);
                PagesWithTableSchema.Add(BlockNumber);
            }

            foreach (var TablePageNumber in PagesWithTableSchema)
            {
                int Position = TablePageNumber * 256;
                int NextBlock = BitConverter.ToInt32(BytesTableStructure, Position);
                short StringLen = BitConverter.ToInt16(BytesTableStructure, Position + 4);
                string StrDefinition = Encoding.UTF8.GetString(BytesTableStructure, Position + 6, StringLen);
                while (NextBlock > 0)
                {
                    Position = NextBlock * 256;
                    NextBlock = BitConverter.ToInt32(BytesTableStructure, Position);
                    StringLen = BitConverter.ToInt16(BytesTableStructure, Position + 4);
                    StrDefinition += Encoding.UTF8.GetString(BytesTableStructure, Position + 6, StringLen);
                }

                var TableDefinition = ParserServices.ParsesClass.ParseString(StrDefinition);
                if ((TableDefinition[0][0].ToString().ToUpper() ?? "") == TargetTable)
                {
                    Page.TableDefinition = StrDefinition;
                    CommonModule.ParseTableDefinition(ref Page);
                    break;
                }
            }

            return Page;
        }

        private static void ReadAllRecordsFromStoragePages(ref AccessFunctions.PageParams PageHeader, BinaryReader reader)
        {
            int FirstPage = PageHeader.BlockData;
            int BlockBlob = PageHeader.BlockBlob;
            int PageSize = PageHeader.PageSize;
            PageHeader.Records = new List<Dictionary<string, object>>();
            var bytesBlock1 = new byte[PageSize];
            reader.BaseStream.Seek(FirstPage * PageSize, SeekOrigin.Begin);
            reader.Read(bytesBlock1, 0, PageSize);
            var DataPage = ReadObjectPageDefinition(reader, bytesBlock1, PageSize);
            DataPage.BinaryData = ReadAllStoragePagesForObject(reader, DataPage);
            var bytesBlock = DataPage.BinaryData;
            int Size = (int)Math.Round(DataPage.Length / (double)PageHeader.RowSize);
            for (int i = 1; i < Size; i++)
            {
                int Pos = PageHeader.RowSize * i;
                int FieldStartPos = 0;
                bool IsDeleted = BitConverter.ToBoolean(bytesBlock, Pos);
                var Dict = new Dictionary<string, object>();
                Dict.Add("IsDeleted", IsDeleted);
                foreach (var Field in PageHeader.Fields)
                {
                    int Pos1 = Pos + 1 + FieldStartPos;
                    if (Field.Name == "PASSWORD")
                    {
                        Dict.Add("OFFSET_PASSWORD", Pos1);
                    }

                    object BytesVal = null;
                    if (Field.Type == "B")
                    {
                        string Strguid = Convert.ToBase64String(bytesBlock, Pos1 + Field.CouldBeNull, Field.Size - Field.CouldBeNull);
                        BytesVal = Convert.FromBase64String(Strguid);
                    }

                    // Dim G = Convert.

                    else if (Field.Type == "L")
                    {
                        BytesVal = BitConverter.ToBoolean(bytesBlock, Pos1 + Field.CouldBeNull);
                    }
                    else if (Field.Type == "DT")
                    {
                        var BytesDate = new byte[7]; // 7 байт
                        for (int AA = 0; AA <= 6; AA++)
                            BytesDate[AA] = Convert.ToByte(Convert.ToString(bytesBlock[Pos1 + AA], 16));
                        try
                        {
                            BytesVal = new DateTime((BytesDate[0] * 100) + BytesDate[1],
                                                    BytesDate[2], 
                                                    BytesDate[3], 
                                                    BytesDate[4], 
                                                    BytesDate[5], 
                                                    BytesDate[6]);
                        }
                        catch (Exception)
                        {
                            BytesVal = "";
                        }
                    }
                    else if (Field.Type == "I")
                    {
                        // двоичные данные неограниченной длины
                        // в рамках хранилища 8.3.6 их быть не должно


                        int DataPos = BitConverter.ToInt32(bytesBlock, Pos1);
                        int DataSize = BitConverter.ToInt32(bytesBlock, Pos1 + 4);
                        if (Field.Name == "DATA")
                        {
                            Dict.Add("DATA_POS", DataPos);
                            Dict.Add("DATA_SIZE", DataSize);
                        }

                        // Dim BytesValTemp = GetBlodDataByIndex(BlockBlob, DataPos, DataSize, reader, PageSize)
                        var BytesBlobBlock = new byte[PageSize];
                        reader.BaseStream.Seek(BlockBlob * PageSize, SeekOrigin.Begin);
                        reader.Read(BytesBlobBlock, 0, PageSize);
                        var BlobPage = ReadObjectPageDefinition(reader, BytesBlobBlock, PageSize);
                        BlobPage.BinaryData = ReadAllStoragePagesForObject(reader, BlobPage);
                        int[] argDataPositions = null;
                        var BytesValTemp = GetCleanDataFromBlob(DataPos, DataSize, BlobPage.BinaryData, DataPositions: ref argDataPositions);
                        // ***************************************


                        var DataKey = new byte[1];
                        int DataKeySize = 0;
                        BytesVal = CommonModule.DecodePasswordStructure(BytesValTemp, ref DataKeySize, ref DataKey);
                        Dict.Add("DATA_KEYSIZE", DataKeySize);
                        Dict.Add("DATA_KEY", DataKey);
                        Dict.Add("DATA_BINARY", BytesValTemp);
                    }
                    else if (Field.Type == "NT")
                    {
                        // Строка неограниченной длины
                        BytesVal = ""; // TODO
                    }
                    else if (Field.Type == "N")
                    {
                        // число
                        BytesVal = 0;
                        string StrNumber = "";
                        for (int AA = 0; AA < Field.Size; AA++)
                        {
                            var character = Convert.ToString(bytesBlock[Pos1 + AA], 16);
                            StrNumber = StrNumber + (character.Length == 1 ? "0" : "") + character;
                        }

                        string FirstSimbol = StrNumber.Substring(0, 1);
                        StrNumber = StrNumber.Substring(1, Field.Length);
                        if (string.IsNullOrEmpty(StrNumber))
                        {
                            BytesVal = 0;
                        }
                        else
                        {
                            BytesVal = Convert.ToInt32(StrNumber) / (Field.Precision > 0 ? Field.Precision * 10 : 1);
                            if (FirstSimbol == "0")
                            {
                                BytesVal = (int)BytesVal * -1;
                            }
                        }
                    }
                    else if (Field.Type == "NVC")
                    {
                        // Строка переменной длины
                        var BytesStr = new byte[2];
                        for (int AA = 0; AA <= 1; AA++)
                            BytesStr[AA] = bytesBlock[Pos1 + AA + Field.CouldBeNull];
                        int L = Math.Min(Field.Size, (BytesStr[0] + BytesStr[1] * 256) * 2);
                        BytesVal = Encoding.Unicode.GetString(bytesBlock, Pos1 + 2 + Field.CouldBeNull, Convert.ToInt32(L)).Trim(); // was L- 2
                    }
                    else if (Field.Type == "NC")
                    {
                        // строка фиксированной длины
                        BytesVal = Encoding.Unicode.GetString(bytesBlock, Pos1, Field.Size);
                    }

                    Dict.Add(Field.Name, BytesVal);
                    FieldStartPos += Field.Size;
                }

                PageHeader.Records.Add(Dict);
            }
        }

        private static byte[] GetCleanDataFromBlob(int Dataindex, int Datasize, byte[] bytesBlock, [Optional, DefaultParameterValue(null)] ref int[] DataPositions)
        {
            int NextBlock = 999; // any number gt 0
            int Pos = Dataindex * 256;
            var ByteBlock = new byte[Datasize];
            int i = 0;
            int BlocksCount = 0;
            while (NextBlock > 0)
            {
                NextBlock = BitConverter.ToInt32(bytesBlock, Pos);
                short BlockSize = BitConverter.ToInt16(bytesBlock, Pos + 4);
                Array.Resize(ref DataPositions, BlocksCount + 1);
                DataPositions[BlocksCount] = Pos + 6;
                for (int j = 0; j < BlockSize; j++)
                {
                    ByteBlock[i] = bytesBlock[Pos + 6 + j];
                    i++;
                }

                Pos = NextBlock * 256;
                BlocksCount++;
            }

            return ByteBlock;
        }

        private static byte[] ReadAllStoragePagesForObject(BinaryReader reader, AccessFunctions.PageParams Page)
        {
            int PagesCountTableStructure = Page.PagesNum.Count;
            var BytesTableStructure = new byte[(PagesCountTableStructure * Page.PageSize)];
            int i = 0;
            foreach (var blk in Page.PagesNum)
            {
                var bytesBlock = new byte[Page.PageSize];
                reader.BaseStream.Seek(blk * Page.PageSize, SeekOrigin.Begin);
                reader.Read(bytesBlock, 0, Page.PageSize);
                for (int a = 0; a < Page.PageSize; a++)
                    BytesTableStructure[i + a] = bytesBlock[a];
                i += Page.PageSize;
            }

            return BytesTableStructure;
        }

        private static AccessFunctions.PageParams ReadObjectPageDefinition(BinaryReader reader, byte[] Bytes, int PageSize = 4096)
        {

            // struct {
            // unsigned int object_type; //0xFD1C или 0x01FD1C 
            // unsigned Int version1; 
            // unsigned Int version2; 
            // unsigned Int version3; 
            // unsigned Long int length; //64-разрядное целое! 
            // unsigned Int pages[]; 
            // }

            var Page = new AccessFunctions.PageParams() { PageSize = PageSize };
            Page.PageType = BitConverter.ToInt32(Bytes, 0);
            Page.version = BitConverter.ToInt32(Bytes, 4);
            Page.version1 = BitConverter.ToInt32(Bytes, 8);
            Page.version2 = BitConverter.ToInt32(Bytes, 12);
            if (Page.PageType == 0xFD1C)
            {
                // 0xFD1C small storage table
                // ???
            }
            else if (Page.PageType == 0x1FD1C)
            {
                // 0x01FD1C  large storage table
                // ???
            }

            Page.Length = BitConverter.ToInt64(Bytes, 16);
            int Index = 24;
            Page.PagesNum = new List<int>();

            // Получим номера страниц размещения 
            while (true)
            {
                int blk = BitConverter.ToInt32(Bytes, Index);
                if (blk == 0)
                {
                    break;
                }

                Page.PagesNum.Add(blk);
                Index += 4;
                if (Index > PageSize - 4)
                {
                    break;
                }
            }

            return Page;
        }

        public static void WritePasswordIntoInfoBaseIB(string FileName, AccessFunctions.PageParams PageHeader, byte[] UserID, byte[] OldData, byte[] NewData, int DataPos, int DataSize)
        {
            var fs = new FileStream(FileName, FileMode.Open, FileAccess.ReadWrite, FileShare.Write);
            var reader = new BinaryReader(fs);
            int PageSize = PageHeader.PageSize;
            int BlockBlob = PageHeader.BlockBlob;
            var BytesBlobBlock = new byte[PageSize];
            reader.BaseStream.Seek(BlockBlob * PageSize, SeekOrigin.Begin);
            reader.Read(BytesBlobBlock, 0, PageSize);
            var BlobPage = ReadObjectPageDefinition(reader, BytesBlobBlock, PageSize);
            BlobPage.BinaryData = ReadAllStoragePagesForObject(reader, BlobPage);
            reader.Close();
            int[] DataPositions = null;
            var BytesValTemp = GetCleanDataFromBlob(DataPos, DataSize, BlobPage.BinaryData, ref DataPositions);
            if (BytesValTemp.SequenceEqual(OldData))
            {
                if (OldData.Count() == NewData.Count())
                {
                    int CurrentByte = 0;
                    // Data is stored in 256 bytes blocks (6 bytes reserved for next block number and size)
                    foreach (var Position in DataPositions)
                    {
                        for (int i = 0; i <= 249; i++)
                        {
                            if (CurrentByte > NewData.Count() - 1)
                            {
                                break;
                            }

                            int NewPosition = Position + i;
                            BlobPage.BinaryData[NewPosition] = NewData[CurrentByte];
                            CurrentByte++;
                        }
                    }

                    // Blob page(s) has been modified. Let's write it back to database
                    fs = new FileStream(FileName, FileMode.Open, FileAccess.ReadWrite, FileShare.Write);
                    var writer = new BinaryWriter(fs);
                    CurrentByte = 0;
                    foreach (var Position in BlobPage.PagesNum)
                    {
                        var TempBlock = new byte[PageSize];
                        for (int j = 0; j < PageSize; j++)
                        {
                            TempBlock[j] = BlobPage.BinaryData[CurrentByte];
                            CurrentByte++;
                        }

                        writer.Seek(Position * PageSize, SeekOrigin.Begin);
                        writer.Write(TempBlock);
                    }

                    writer.Close();
                }
                else
                {
                    throw new Exception("Новый байтовый массив должен совпадать по размерам со старым массивом (т.к. мы только заменяем хэши одинаковой длины)." + Environment.NewLine + "Сообщите пожалуйста об этой ошибке!");
                }
            }
            else
            {
                throw new Exception("Информация в БД была изменена другим процессом! Прочитайте список пользователей заново.");
            }
        }
    }
}