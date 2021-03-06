// Copyright (c) Microsoft Corporation.  All Rights Reserved.  See License.txt in the project root for license information.

/// Byte arrays
namespace FSharp.Compiler.AbstractIL.Internal

open System
open System.IO
#if !FABLE_COMPILER
open System.IO.MemoryMappedFiles
open System.Runtime.InteropServices
open System.Runtime.CompilerServices
open FSharp.NativeInterop
#endif

#nowarn "9"

module Utils =
    let runningOnMono =
    #if ENABLE_MONO_SUPPORT
        // Officially supported way to detect if we are running on Mono.
        // See http://www.mono-project.com/FAQ:_Technical
        // "How can I detect if am running in Mono?" section
        try
            System.Type.GetType ("Mono.Runtime") <> null
        with _ ->
            // Must be robust in the case that someone else has installed a handler into System.AppDomain.OnTypeResolveEvent
            // that is not reliable.
            // This is related to bug 5506--the issue is actually a bug in VSTypeResolutionService.EnsurePopulated which is
            // called by OnTypeResolveEvent. The function throws a NullReferenceException. I'm working with that team to get
            // their issue fixed but we need to be robust here anyway.
            false
    #else
        false
    #endif

module internal Bytes = 
    let b0 n =  (n &&& 0xFF)
    let b1 n =  ((n >>> 8) &&& 0xFF)
    let b2 n =  ((n >>> 16) &&& 0xFF)
    let b3 n =  ((n >>> 24) &&& 0xFF)

    let dWw1 n = int32 ((n >>> 32) &&& 0xFFFFFFFFL)
    let dWw0 n = int32 (n          &&& 0xFFFFFFFFL)

    let get (b:byte[]) n = int32 (Array.get b n)  
    let zeroCreate n : byte[] = Array.zeroCreate n      

    let sub ( b:byte[]) s l = Array.sub b s l   
    let blit (a:byte[]) b c d e = Array.blit a b c d e 

    let ofInt32Array (arr:int[]) = Array.init arr.Length (fun i -> byte arr.[i]) 

    let stringAsUtf8NullTerminated (s:string) = 
        Array.append (System.Text.Encoding.UTF8.GetBytes s) (ofInt32Array [| 0x0 |]) 

    let stringAsUnicodeNullTerminated (s:string) = 
        Array.append (System.Text.Encoding.Unicode.GetBytes s) (ofInt32Array [| 0x0;0x0 |])

[<AbstractClass>]
type ByteMemory () =

#if FABLE_COMPILER
    abstract Item: int -> byte with get
#else
    abstract Item: int -> byte with get, set
#endif

    abstract Length: int

    abstract ReadBytes: pos: int * count: int -> byte[]

    abstract ReadInt32: pos: int -> int

    abstract ReadUInt16: pos: int -> uint16

    abstract ReadUtf8String: pos: int * count: int -> string

    abstract Slice: pos: int * count: int -> ByteMemory

#if !FABLE_COMPILER
    abstract CopyTo: Stream -> unit
#endif

    abstract Copy: srcOffset: int * dest: byte[] * destOffset: int * count: int -> unit

    abstract ToArray: unit -> byte[]

#if !FABLE_COMPILER
    abstract AsStream: unit -> Stream

    abstract AsReadOnlyStream: unit -> Stream
#endif

[<Sealed>]
type ByteArrayMemory(bytes: byte[], offset, length) =
    inherit ByteMemory()

    let checkCount count =
        if count < 0 then
            raise (ArgumentOutOfRangeException("count", "Count is less than zero."))

    do
        if length < 0 || length > bytes.Length then
            raise (ArgumentOutOfRangeException("length"))

        if offset < 0 || (offset + length) > bytes.Length then
            raise (ArgumentOutOfRangeException("offset"))
    
    override _.Item 
        with get i = bytes.[offset + i]
#if !FABLE_COMPILER
        and set i v = bytes.[offset + i] <- v
#endif

    override _.Length = length

    override _.ReadBytes(pos, count) = 
        checkCount count
        if count > 0 then
            Array.sub bytes (offset + pos) count
        else
            Array.empty

    override _.ReadInt32 pos =
        let finalOffset = offset + pos
        (uint32 bytes.[finalOffset]) |||
        ((uint32 bytes.[finalOffset + 1]) <<< 8) |||
        ((uint32 bytes.[finalOffset + 2]) <<< 16) |||
        ((uint32 bytes.[finalOffset + 3]) <<< 24)
        |> int

    override _.ReadUInt16 pos =
        let finalOffset = offset + pos
        (uint16 bytes.[finalOffset]) |||
        ((uint16 bytes.[finalOffset + 1]) <<< 8)

    override _.ReadUtf8String(pos, count) =
        checkCount count
        if count > 0 then
            System.Text.Encoding.UTF8.GetString(bytes, offset + pos, count)
        else
            String.Empty

    override _.Slice(pos, count) =
        checkCount count
        if count > 0 then
            ByteArrayMemory(bytes, offset + pos, count) :> ByteMemory
        else
            ByteArrayMemory(Array.empty, 0, 0) :> ByteMemory

#if !FABLE_COMPILER
    override _.CopyTo stream =
        if length > 0 then
            stream.Write(bytes, offset, length)
#endif

    override _.Copy(srcOffset, dest, destOffset, count) =
        checkCount count
        if count > 0 then
            Array.blit bytes (offset + srcOffset) dest destOffset count

    override _.ToArray() =
        if length > 0 then
            Array.sub bytes offset length
        else
            Array.empty

#if !FABLE_COMPILER
    override _.AsStream() =
        if length > 0 then
            new MemoryStream(bytes, offset, length) :> Stream
        else
            new MemoryStream([||], 0, 0, false) :> Stream

    override _.AsReadOnlyStream() =
        if length > 0 then
            new MemoryStream(bytes, offset, length, false) :> Stream
        else
            new MemoryStream([||], 0, 0, false) :> Stream
#endif

#if !FABLE_COMPILER

[<Sealed>]
type SafeUnmanagedMemoryStream =
    inherit UnmanagedMemoryStream

    val mutable private holder: obj
    val mutable private isDisposed: bool

    new (addr, length, holder) =
        {
            inherit UnmanagedMemoryStream(addr, length)
            holder = holder
            isDisposed = false
        }

    new (addr: nativeptr<byte>, length: int64, capacity: int64, access: FileAccess, holder) =
        {
            inherit UnmanagedMemoryStream(addr, length, capacity, access)
            holder = holder
            isDisposed = false
        }

    override x.Dispose disposing =
        base.Dispose disposing
        x.holder <- null // Null out so it can be collected.

[<Sealed>]
type RawByteMemory(addr: nativeptr<byte>, length: int, holder: obj) =
    inherit ByteMemory ()

    let check i =
        if i < 0 || i >= length then 
            raise (ArgumentOutOfRangeException("i"))

    let checkCount count =
        if count < 0 then
            raise (ArgumentOutOfRangeException("count", "Count is less than zero."))

    do
        if length < 0 then
            raise (ArgumentOutOfRangeException("length"))

    override _.Item 
        with get i =
            check i
            NativePtr.add addr i
            |> NativePtr.read 
        and set i v =
            check i
            NativePtr.set addr i v

    override _.Length = length

    override _.ReadUtf8String(pos, count) =
        checkCount count
        if count > 0 then
            check pos
            check (pos + count - 1)
            System.Text.Encoding.UTF8.GetString(NativePtr.add addr pos, count)
        else
            String.Empty

    override _.ReadBytes(pos, count) = 
        checkCount count
        if count > 0 then
            check pos
            check (pos + count - 1)
            let res = Bytes.zeroCreate count
            Marshal.Copy(NativePtr.toNativeInt addr + nativeint pos, res, 0, count)
            res
        else
            Array.empty

    override _.ReadInt32 pos =
        check pos
        check (pos + 3)
        Marshal.ReadInt32(NativePtr.toNativeInt addr + nativeint pos)

    override _.ReadUInt16 pos =
        check pos
        check (pos + 1)
        uint16(Marshal.ReadInt16(NativePtr.toNativeInt addr + nativeint pos))

    override _.Slice(pos, count) =
        checkCount count
        if count > 0 then
            check pos
            check (pos + count - 1)
            RawByteMemory(NativePtr.add addr pos, count, holder) :> ByteMemory
        else
            ByteArrayMemory(Array.empty, 0, 0) :> ByteMemory

    override x.CopyTo stream =
        if length > 0 then
            use stream2 = x.AsStream()
            stream2.CopyTo stream

    override _.Copy(srcOffset, dest, destOffset, count) =
        checkCount count
        if count > 0 then
            check srcOffset
            Marshal.Copy(NativePtr.toNativeInt addr + nativeint srcOffset, dest, destOffset, count)

    override _.ToArray() =
        if length > 0 then
            let res = Array.zeroCreate<byte> length
            Marshal.Copy(NativePtr.toNativeInt addr, res, 0, res.Length)
            res
        else
            Array.empty

    override _.AsStream() =
        if length > 0 then
            new SafeUnmanagedMemoryStream(addr, int64 length, holder) :> Stream
        else
            new MemoryStream([||], 0, 0, false) :> Stream

    override _.AsReadOnlyStream() =
        if length > 0 then
            new SafeUnmanagedMemoryStream(addr, int64 length, int64 length, FileAccess.Read, holder) :> Stream
        else
            new MemoryStream([||], 0, 0, false) :> Stream

#endif //!FABLE_COMPILER

[<Struct;NoEquality;NoComparison>]
type ReadOnlyByteMemory(bytes: ByteMemory) =

    member _.Item with get i = bytes.[i]

    member _.Length with get () = bytes.Length

    member _.ReadBytes(pos, count) = bytes.ReadBytes(pos, count)

    member _.ReadInt32 pos = bytes.ReadInt32 pos

    member _.ReadUInt16 pos = bytes.ReadUInt16 pos

    member _.ReadUtf8String(pos, count) = bytes.ReadUtf8String(pos, count)

    member _.Slice(pos, count) = bytes.Slice(pos, count) |> ReadOnlyByteMemory

#if !FABLE_COMPILER
    member _.CopyTo stream = bytes.CopyTo stream
#endif

    member _.Copy(srcOffset, dest, destOffset, count) = bytes.Copy(srcOffset, dest, destOffset, count)

    member _.ToArray() = bytes.ToArray()

#if !FABLE_COMPILER
    member _.AsStream() = bytes.AsReadOnlyStream()
#endif

type ByteMemory with

    member x.AsReadOnly() = ReadOnlyByteMemory x

#if !FABLE_COMPILER
    static member CreateMemoryMappedFile(bytes: ReadOnlyByteMemory) =
        if Utils.runningOnMono
        then
            // mono's MemoryMappedFile implementation throws with null `mapName`, so we use byte arrays instead: https://github.com/mono/mono/issues/10245
            ByteArrayMemory.FromArray (bytes.ToArray()) :> ByteMemory
        else
            let length = int64 bytes.Length
            let mmf =
                let mmf = MemoryMappedFile.CreateNew(null, length, MemoryMappedFileAccess.ReadWrite, MemoryMappedFileOptions.None, HandleInheritability.None)
                use stream = mmf.CreateViewStream(0L, length, MemoryMappedFileAccess.ReadWrite)
                bytes.CopyTo stream
                mmf

            let accessor = mmf.CreateViewAccessor(0L, length, MemoryMappedFileAccess.ReadWrite)
            RawByteMemory.FromUnsafePointer(accessor.SafeMemoryMappedViewHandle.DangerousGetHandle(), int length, (mmf, accessor))

    static member FromFile(path, access, ?canShadowCopy: bool) =
        let canShadowCopy = defaultArg canShadowCopy false

        if Utils.runningOnMono
        then
            // mono's MemoryMappedFile implementation throws with null `mapName`, so we use byte arrays instead: https://github.com/mono/mono/issues/10245
            let bytes = File.ReadAllBytes path
            ByteArrayMemory.FromArray bytes
        else
            let memoryMappedFileAccess =
                match access with
                | FileAccess.Read -> MemoryMappedFileAccess.Read
                | FileAccess.Write -> MemoryMappedFileAccess.Write
                | _ -> MemoryMappedFileAccess.ReadWrite

            let fileStream = File.Open(path, FileMode.Open, access, FileShare.Read)

            let length = fileStream.Length

            let mmf, accessor, length =
                let mmf =
                    if canShadowCopy then
                        let mmf =
                            MemoryMappedFile.CreateNew(
                                null,
                                length,
                                MemoryMappedFileAccess.ReadWrite,
                                MemoryMappedFileOptions.None,
                                HandleInheritability.None)
                        use stream = mmf.CreateViewStream(0L, length, MemoryMappedFileAccess.ReadWrite)
                        fileStream.CopyTo(stream)
                        fileStream.Dispose()
                        mmf
                    else
                        MemoryMappedFile.CreateFromFile(
                            fileStream,
                            null,
                            length,
                            memoryMappedFileAccess,
                            HandleInheritability.None,
                            leaveOpen=false)
                mmf, mmf.CreateViewAccessor(0L, length, memoryMappedFileAccess), length

            // Validate MMF with the access that was intended.
            match access with
            | FileAccess.Read when not accessor.CanRead -> invalidOp "Cannot read file"
            | FileAccess.Write when not accessor.CanWrite -> invalidOp "Cannot write file"
            | FileAccess.ReadWrite when not accessor.CanRead || not accessor.CanWrite -> invalidOp "Cannot read or write file"
            | _ -> ()

            RawByteMemory.FromUnsafePointer(accessor.SafeMemoryMappedViewHandle.DangerousGetHandle(), int length, (mmf, accessor))

    static member FromUnsafePointer(addr, length, holder: obj) = 
        RawByteMemory(NativePtr.ofNativeInt addr, length, holder) :> ByteMemory
#endif //!FABLE_COMPILER

    static member FromArray(bytes, offset, length) =
        ByteArrayMemory(bytes, offset, length) :> ByteMemory

    static member FromArray bytes =
        ByteArrayMemory.FromArray(bytes, 0, bytes.Length)

type internal ByteStream = 
    { bytes: ReadOnlyByteMemory
      mutable pos: int 
      max: int }
    member b.ReadByte() = 
        if b.pos >= b.max then failwith "end of stream"
        let res = b.bytes.[b.pos]
        b.pos <- b.pos + 1
        res 
    member b.ReadUtf8String n = 
        let res = b.bytes.ReadUtf8String(b.pos,n)  
        b.pos <- b.pos + n; res 
      
    static member FromBytes (b: ReadOnlyByteMemory,n,len) = 
        if n < 0 || (n+len) > b.Length then failwith "FromBytes"
        { bytes = b; pos = n; max = n+len }

    member b.ReadBytes n  = 
        if b.pos + n > b.max then failwith "ReadBytes: end of stream"
        let res = b.bytes.Slice(b.pos, n)
        b.pos <- b.pos + n
        res

    member b.Position = b.pos 
#if LAZY_UNPICKLE
    member b.CloneAndSeek = { bytes=b.bytes; pos=pos; max=b.max }
    member b.Skip = b.pos <- b.pos + n
#endif


type internal ByteBuffer = 
    { mutable bbArray: byte[] 
      mutable bbCurrent: int }

    member buf.Ensure newSize = 
        let oldBufSize = buf.bbArray.Length 
        if newSize > oldBufSize then 
            let old = buf.bbArray 
            buf.bbArray <- Bytes.zeroCreate (max newSize (oldBufSize * 2))
            Bytes.blit old 0 buf.bbArray 0 buf.bbCurrent

    member buf.Close () = Bytes.sub buf.bbArray 0 buf.bbCurrent

    member buf.EmitIntAsByte (i:int) = 
        let newSize = buf.bbCurrent + 1 
        buf.Ensure newSize
        buf.bbArray.[buf.bbCurrent] <- byte i
        buf.bbCurrent <- newSize 

    member buf.EmitByte (b:byte) = buf.EmitIntAsByte (int b)

    member buf.EmitIntsAsBytes (arr:int[]) = 
        let n = arr.Length
        let newSize = buf.bbCurrent + n 
        buf.Ensure newSize
        let bbArr = buf.bbArray
        let bbBase = buf.bbCurrent
        for i = 0 to n - 1 do 
            bbArr.[bbBase + i] <- byte arr.[i] 
        buf.bbCurrent <- newSize 

    member bb.FixupInt32 pos n = 
        bb.bbArray.[pos] <- (Bytes.b0 n |> byte)
        bb.bbArray.[pos + 1] <- (Bytes.b1 n |> byte)
        bb.bbArray.[pos + 2] <- (Bytes.b2 n |> byte)
        bb.bbArray.[pos + 3] <- (Bytes.b3 n |> byte)

    member buf.EmitInt32 n = 
        let newSize = buf.bbCurrent + 4 
        buf.Ensure newSize
        buf.FixupInt32 buf.bbCurrent n
        buf.bbCurrent <- newSize 

    member buf.EmitBytes (i:byte[]) = 
        let n = i.Length 
        let newSize = buf.bbCurrent + n 
        buf.Ensure newSize
        Bytes.blit i 0 buf.bbArray buf.bbCurrent n
        buf.bbCurrent <- newSize 

    member buf.EmitByteMemory (i:ReadOnlyByteMemory) = 
        let n = i.Length 
        let newSize = buf.bbCurrent + n 
        buf.Ensure newSize
        i.Copy(0, buf.bbArray, buf.bbCurrent, n)
        buf.bbCurrent <- newSize 

    member buf.EmitInt32AsUInt16 n = 
        let newSize = buf.bbCurrent + 2 
        buf.Ensure newSize
        buf.bbArray.[buf.bbCurrent] <- (Bytes.b0 n |> byte)
        buf.bbArray.[buf.bbCurrent + 1] <- (Bytes.b1 n |> byte)
        buf.bbCurrent <- newSize 
    
    member buf.EmitBoolAsByte (b:bool) = buf.EmitIntAsByte (if b then 1 else 0)

    member buf.EmitUInt16 (x:uint16) = buf.EmitInt32AsUInt16 (int32 x)

    member buf.EmitInt64 x = 
        buf.EmitInt32 (Bytes.dWw0 x)
        buf.EmitInt32 (Bytes.dWw1 x)

    member buf.Position = buf.bbCurrent

    static member Create sz = 
        { bbArray=Bytes.zeroCreate sz 
          bbCurrent = 0 }


