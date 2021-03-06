﻿using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using LibOpenNFS.Core;
using LibOpenNFS.DataModels;
using LibOpenNFS.Utils;

namespace LibOpenNFS.Games.UG2.TrackStreamer.Readers
{
    public class SolidListReadContainer : ReadContainer<SolidList>
    {
        /**
 * Vertex24: basic vertex. xyz coords, color, UV, that's it
 */
        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        private struct Vertex24
        {
            public readonly float X;

            public readonly float Y;

            public readonly float Z;

            public readonly int Color;

            public readonly float U;

            public readonly float V;
        }

        /**
 * Vertex36: Used in cars, similar to Vertex24 but with 3 extra floats
 */
        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        private struct Vertex36
        {
            public readonly float X;

            public readonly float Y;

            public readonly float Z;

            public readonly float nX;

            public readonly float nY;

            public readonly float nZ;

            public readonly int Color;

            public readonly float U;

            public readonly float V;
        }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        private struct FaceStruct
        {
            public readonly short VertexA;
            public readonly short VertexB;
            public readonly short VertexC;
        }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        private struct ObjectHeader
        {
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)]
            private readonly byte[] zero;

            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)]
            private readonly byte[] pad;

            public readonly uint NumTris;

            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 8)]
            private readonly byte[] pad2;

            public readonly CommonStructs.Point3D MinPoint;
            public readonly CommonStructs.Point3D MaxPoint;
            public readonly CommonStructs.Matrix Matrix;

            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 9)]
            public readonly uint[] unknown;
        }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        private struct FileInfo
        {
            private readonly uint blank1;
            private readonly uint blank2;
            private readonly uint tagA;
            private readonly uint tagB;

            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 56)]
            public readonly string Path;

            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 8)]
            public readonly string Section;

            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 6)]
            private readonly uint[] unknownData;

            private readonly uint unkVarA;
            private readonly uint unkVarB;
            private readonly uint unkVarC;

            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 7)]
            private readonly uint[] unknownData2;
        }

        private enum SolidListChunks : long
        {
            Header = 0x80134001,
            FileInfo = 0x00134002,
            HashTable = 0x00134003,
            Object = 0x80134010,
            ObjectHeader = 0x00134011,
            MeshHeader = 0x80134100,
            MeshVertices = 0x00134b01,
            MeshFaces = 0x00134b03,
            TextureRefs = 0x00134012,
            Material = 0x00134B02
        }

        public SolidListReadContainer(BinaryReader binaryReader, string fileName, long? containerSize)
            : base(binaryReader, fileName, containerSize)
        {
        }

        public override SolidList Get()
        {
            if (ContainerSize == 0)
            {
                throw new Exception("containerSize is not set!");
            }

            _solidList = new SolidList(ChunkID.BCHUNK_SPEED_ESOLID_LIST_CHUNKS, ContainerSize,
                BinaryReader.BaseStream.Position);

            ReadChunks(ContainerSize);

            return _solidList;
        }

        protected override void ReadChunks(long totalSize)
        {
            var runTo = BinaryReader.BaseStream.Position + totalSize;

            for (var i = 0; i < 0xFFFF && BinaryReader.BaseStream.Position < runTo; i++)
            {
                var chunkId = BinaryReader.ReadUInt32();
                var chunkSize = BinaryReader.ReadUInt32();
                var normalizedId = (long) (chunkId & 0xffffffff);

                BinaryUtil.ReadPadding(BinaryReader, ref chunkSize);

                var chunkRunTo = BinaryReader.BaseStream.Position + chunkSize;

                BinaryUtil.PrintID(BinaryReader, chunkId, normalizedId, chunkSize, GetType(), _logLevel,
                    typeof(SolidListChunks));

                switch (normalizedId)
                {
                    case (long) SolidListChunks.Header:
                    {
                        goto case (long) SolidListChunks.Object;
                    }
                    case (long) SolidListChunks.Object:
                    {
                        _logLevel = 2;
                        ReadChunks(chunkSize);
                        _logLevel = 1;
                        break;
                    }
                    case (long) SolidListChunks.MeshHeader:
                    {
                        _solidList.LastObject.Mesh = new SolidMesh();

                        _logLevel = 3;
                        ReadChunks(chunkSize);
                        _logLevel = 2;
                        break;
                    }
                    case (long) SolidListChunks.FileInfo:
                    {
                        var fileInfo = BinaryUtil.ReadStruct<FileInfo>(BinaryReader);

                        _solidList.Path = fileInfo.Path;
                        _solidList.SectionId = fileInfo.Section;

                        break;
                    }
                    case (long) SolidListChunks.HashTable:
                    {
                        // each hash entry is 8 bytes: 4 bytes for the hash and 4 bytes of 0x00

                        var numEntries = chunkSize / 8;

                        for (var j = 0; j < numEntries; j++)
                        {
                            _solidList.Hashes.Add(BinaryReader.ReadUInt32());
                            BinaryReader.BaseStream.Seek(4, SeekOrigin.Current);
                        }

                        break;
                    }
                    case (long) SolidListChunks.TextureRefs: // Texture hashes
                    {
                        // Every entry is 8 bytes; a 4-byte hash and 4 bytes of 0x00.
                        var numTextures = chunkSize / 8;

                        for (var j = 0; j < numTextures; j++)
                        {
                            var hash = BinaryReader.ReadUInt32();
                            BinaryReader.BaseStream.Seek(4, SeekOrigin.Current);
                            _solidList.LastObject.Textures.Add(hash);
                        }

                        break;
                    }
                    case (long) SolidListChunks.ObjectHeader:
                    {
                        DebugUtil.EnsureCondition(
                            _solidList.Hashes.Count >= _solidList.Objects.Count,
                            () =>
                                $"Expected enough hashes for {_solidList.Objects.Count} object(s); we only have {_solidList.Hashes.Count}");

                        var objectHeader = BinaryUtil.ReadStruct<ObjectHeader>(BinaryReader);
                        var objectName = BinaryUtil.ReadNullTerminatedString(BinaryReader);

                        var objectHash = _solidList.Hashes[_solidList.Objects.Count];
                        
                        _solidList.Objects.Add(new SolidObject
                        {
                            Hash = objectHash,
                            Matrix = objectHeader.Matrix,
                            MaxPoint = objectHeader.MaxPoint,
                            MinPoint = objectHeader.MinPoint,
                            Name = objectName
                        });

                        break;
                    }
                    case (long) SolidListChunks.MeshFaces:
                    {
                        for (var j = 0; j < BinaryUtil.ComputeEntryCount<FaceStruct>(chunkSize); j++)
                        {
                            var face = BinaryUtil.ReadStruct<FaceStruct>(BinaryReader);

                            _solidList.LastObject.Mesh.Faces.Add(new Face
                            {
                                VertexA = face.VertexA,
                                VertexB = face.VertexB,
                                VertexC = face.VertexC,
                            });
                        }

                        break;
                    }
                    case (long) SolidListChunks.MeshVertices:
                    {
                        var startPos = BinaryReader.BaseStream.Position;

                        // I spy, with my little eye, a bad hack that shouldn't exist
                        if (_solidList.Path.Contains(@"CARS\") && _solidList.SectionId == "DEFAULT")
                        {
                            DebugUtil.EnsureCondition(ReadVertex36(BinaryUtil.ComputeEntryCount<Vertex36>(chunkSize)),
                                () => "Failed to read vertices properly!");
                        }
                        else
                        {
                            if (!ReadVertex24(BinaryUtil.ComputeEntryCount<Vertex24>(chunkSize)))
                            {
                                BinaryReader.BaseStream.Position = startPos;
                            
                                DebugUtil.EnsureCondition(ReadVertex36(BinaryUtil.ComputeEntryCount<Vertex36>(chunkSize)),
                                    () => "Failed to read vertices properly!");
                            }
                        }

                        break;
                    }
                }

                BinaryUtil.ValidatePosition(BinaryReader, chunkRunTo, GetType());
                BinaryReader.BaseStream.Seek(chunkRunTo - BinaryReader.BaseStream.Position, SeekOrigin.Current);
            }
        }

        private SolidList _solidList;

        private readonly long[] _paddedChunks =
        {
            (long) SolidListChunks.ObjectHeader,
            (long) SolidListChunks.MeshVertices,
            (long) SolidListChunks.Material,
        };

        private int _logLevel = 1;

        private bool ReadVertex24(long count)
        {
            var success = true;
            
            for (var j = 0; j < count; j++)
            {
                var vertex = BinaryUtil.ReadStruct<Vertex24>(BinaryReader);

                _solidList.LastObject.Mesh.Vertices.Add(new Vertex
                {
                    Color = vertex.Color,
                    X = vertex.X,
                    Y = vertex.Y,
                    Z = vertex.Z,
                    U = vertex.U,
                    V = vertex.V,
                });

                if (!float.IsNaN(vertex.X)) continue;

                success = false;
                _solidList.LastObject.Mesh.Vertices.Clear();
                break;
            }

            return success;
        }
        
        private bool ReadVertex36(long count)
        {
            var success = true;
            
            for (var j = 0; j < count; j++)
            {
                var vertex = BinaryUtil.ReadStruct<Vertex36>(BinaryReader);

                _solidList.LastObject.Mesh.Vertices.Add(new Vertex
                {
                    Color = vertex.Color,
                    X = vertex.X,
                    Y = vertex.Y,
                    Z = vertex.Z,
                    U = vertex.U,
                    V = vertex.V,
                });

                if (!float.IsNaN(vertex.X)) continue;

                success = false;
                _solidList.LastObject.Mesh.Vertices.Clear();
                break;
            }

            return success;
        }
    }
}