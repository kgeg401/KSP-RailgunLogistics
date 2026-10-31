"""Bounded Windows snapshot reads compatible with atomic file replacement."""
import ctypes,os,time
from ctypes import wintypes
from pathlib import Path

_kernel=None

def read_shared_bytes(path,maximum=2*1024*1024):
    global _kernel
    if os.name!='nt':
        value=Path(path).read_bytes()
        if len(value)>maximum:raise ValueError('Snapshot exceeds bounded size')
        return value
    if _kernel is None:
        k=ctypes.WinDLL('kernel32',use_last_error=True)
        k.CreateFileW.argtypes=[wintypes.LPCWSTR,wintypes.DWORD,wintypes.DWORD,ctypes.c_void_p,wintypes.DWORD,wintypes.DWORD,wintypes.HANDLE];k.CreateFileW.restype=wintypes.HANDLE
        k.GetFileSizeEx.argtypes=[wintypes.HANDLE,ctypes.POINTER(ctypes.c_longlong)];k.GetFileSizeEx.restype=wintypes.BOOL
        k.ReadFile.argtypes=[wintypes.HANDLE,ctypes.c_void_p,wintypes.DWORD,ctypes.POINTER(wintypes.DWORD),ctypes.c_void_p];k.ReadFile.restype=wintypes.BOOL
        k.CloseHandle.argtypes=[wintypes.HANDLE];k.CloseHandle.restype=wintypes.BOOL;_kernel=k
    for attempt in range(10):
        handle=_kernel.CreateFileW(str(Path(path).resolve()),0x80000000,7,None,3,0x80,None)
        if handle!=wintypes.HANDLE(-1).value:break
        error=ctypes.get_last_error()
        if error not in (2,5,32) or attempt==9:raise ctypes.WinError(error)
        time.sleep(.002)
    try:
        size=ctypes.c_longlong()
        if not _kernel.GetFileSizeEx(handle,ctypes.byref(size)):raise ctypes.WinError(ctypes.get_last_error())
        if not 0<=size.value<=maximum:raise ValueError('Snapshot exceeds bounded size')
        buffer=ctypes.create_string_buffer(size.value);count=wintypes.DWORD()
        if size.value and not _kernel.ReadFile(handle,buffer,size.value,ctypes.byref(count),None):raise ctypes.WinError(ctypes.get_last_error())
        if count.value!=size.value:raise OSError('Incomplete native snapshot read')
        return buffer.raw[:count.value]
    finally:_kernel.CloseHandle(handle)

def read_shared_text(path,encoding='utf-8-sig'):
    return read_shared_bytes(path).decode(encoding)
