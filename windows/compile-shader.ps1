$ErrorActionPreference = 'Stop'
if (-not ('SnaplineShaderCompiler' -as [type])) {
    Add-Type -TypeDefinition @'
using System;
using System.IO;
using System.Runtime.InteropServices;
public static class SnaplineShaderCompiler {
    [ComImport, Guid("8BA5FB08-5195-40e2-AC58-0D989C3A0102"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface Blob { [PreserveSig] IntPtr GetBufferPointer(); [PreserveSig] UIntPtr GetBufferSize(); }
    [DllImport("d3dcompiler_47.dll", CharSet=CharSet.Ansi)]
    private static extern int D3DCompile([MarshalAs(UnmanagedType.LPStr)] string code, UIntPtr size, string name, IntPtr defines, IntPtr include, string entry, string profile, uint flags, uint flags2, out Blob output, out Blob errors);
    public static void Compile(string input, string target) {
        string code=File.ReadAllText(input); Blob output,errors;
        int result=D3DCompile(code,(UIntPtr)System.Text.Encoding.UTF8.GetByteCount(code),input,IntPtr.Zero,IntPtr.Zero,"main","ps_2_0",32768,0,out output,out errors);
        if(result<0) { string message=errors==null?result.ToString():Marshal.PtrToStringAnsi(errors.GetBufferPointer()); if(errors!=null)Marshal.ReleaseComObject(errors); throw new InvalidOperationException(message); }
        try { var bytes=new byte[(int)output.GetBufferSize()]; Marshal.Copy(output.GetBufferPointer(),bytes,0,bytes.Length); File.WriteAllBytes(target,bytes); }
        finally { Marshal.ReleaseComObject(output); if(errors!=null)Marshal.ReleaseComObject(errors); }
    }
}
'@
}
[SnaplineShaderCompiler]::Compile((Join-Path $PSScriptRoot 'ui\SettingsGlass.hlsl'), (Join-Path $PSScriptRoot 'ui\SettingsGlass.ps'))
