$ErrorActionPreference = 'Stop'
Add-Type -TypeDefinition @'
using System;
using System.IO;
using System.Runtime.InteropServices;
public static class SnaplineToolShaderCompiler {
 [ComImport, Guid("8BA5FB08-5195-40e2-AC58-0D989C3A0102"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
 private interface IBlob { [PreserveSig] IntPtr GetBufferPointer(); [PreserveSig] UIntPtr GetBufferSize(); }
 [DllImport("d3dcompiler_47.dll", CharSet=CharSet.Ansi, ExactSpelling=true)]
 private static extern int D3DCompile(string source, UIntPtr length, string name, IntPtr defines, IntPtr include, string entry, string target, uint flags, uint flags2, out IBlob code, out IBlob error);
 public static void Compile(string sourcePath, string destination) {
  string source=File.ReadAllText(sourcePath); IBlob code=null,error=null;
  try {
   int result=D3DCompile(source,(UIntPtr)source.Length,"ToolSheen.hlsl",IntPtr.Zero,IntPtr.Zero,"main","ps_2_0",1u<<15,0,out code,out error);
   if(result<0) throw new InvalidOperationException(error==null?"Shader compilation failed.":Marshal.PtrToStringAnsi(error.GetBufferPointer()));
   byte[] bytes=new byte[(int)code.GetBufferSize().ToUInt64()]; Marshal.Copy(code.GetBufferPointer(),bytes,0,bytes.Length); File.WriteAllBytes(destination,bytes);
  } finally { if(code!=null)Marshal.ReleaseComObject(code); if(error!=null)Marshal.ReleaseComObject(error); }
 }
}
'@
$ui = Join-Path $PSScriptRoot 'ui'
[SnaplineToolShaderCompiler]::Compile((Join-Path $ui 'ToolSheen.hlsl'), (Join-Path $ui 'ToolSheen.ps'))
