# -*- coding: utf-8 -*-
# @Link    : https://github.com/aicezam/SmartOnmyoji
# @Version : Python3.7.6
# @MIT License Copyright (c) 2022 ACE

import time
from ctypes import byref, c_int, c_uint, windll
from os.path import abspath, dirname
from subprocess import Popen, PIPE

import numpy as np
import win32com.client
from numpy import frombuffer, uint8, array
from win32con import MONITOR_DEFAULTTONEAREST, SRCCOPY
from win32gui import (
    DeleteObject,
    SetForegroundWindow,
    GetWindowRect,
    GetWindowDC,
    GetClientRect,
    ClientToScreen,
)
from win32ui import CreateDCFromHandle, CreateBitmap

# from cv2 import cv2
import cv2
from PIL import ImageGrab

PW_RENDERFULLCONTENT = 0x00000002


from modules.ModuleGetConfig import ReadConfigFile


PROCESS_DPI_UNAWARE = 0
PROCESS_SYSTEM_DPI_AWARE = 1
PROCESS_PER_MONITOR_DPI_AWARE = 2


def _get_process_dpi_awareness():
    """Try obtaining current process DPI awareness level."""
    try:
        awareness = c_int()
        windll.shcore.GetProcessDpiAwareness(0, byref(awareness))
        return awareness.value
    except Exception:
        return None


def _set_process_dpi_awareness():
    """Upgrade process DPI awareness when possible to avoid GDI virtualization."""
    awareness = _get_process_dpi_awareness()
    if awareness == PROCESS_PER_MONITOR_DPI_AWARE:
        return awareness
    try:
        windll.shcore.SetProcessDpiAwareness(PROCESS_PER_MONITOR_DPI_AWARE)
    except Exception:
        try:
            windll.user32.SetProcessDPIAware()
        except Exception:
            pass
    return _get_process_dpi_awareness()


PROCESS_DPI_AWARENESS = _set_process_dpi_awareness()


def _get_window_scale(hwnd):
    """Return dpi scaling factor for specific window, fallback to None when unavailable."""
    if not hwnd:
        return None
    try:
        dpi = windll.user32.GetDpiForWindow(hwnd)
        if dpi:
            return dpi / 96.0
    except Exception:
        pass
    try:
        monitor = windll.user32.MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST)
        dpi_x = c_uint()
        dpi_y = c_uint()
        windll.shcore.GetDpiForMonitor(monitor, 0, byref(dpi_x), byref(dpi_y))
        if dpi_x.value:
            return dpi_x.value / 96.0
    except Exception:
        pass
    return None


class GetScreenCapture:
    def __init__(self, handle_num=0, handle_width=0, handle_height=0):
        super(GetScreenCapture, self).__init__()
        self.hwd_num = handle_num
        self.screen_width = handle_width
        self.screen_height = handle_height
        self.manual_scale_rate = get_screen_scale_rate()
        self.auto_scale_rate = _get_window_scale(self.hwd_num)

    def _current_scale(self):
        if self.hwd_num:
            latest = _get_window_scale(self.hwd_num)
            if latest:
                self.auto_scale_rate = latest
        return self.auto_scale_rate or self.manual_scale_rate or 1.0

    def _capture_dimensions(self):
        """计算BitBlt所需的真实像素尺寸，避免DPI缩放导致的截取不完整。"""
        # 直接使用窗口尺寸，不进行DPI缩放
        # 因为GetWindowRect已经返回的是物理像素，不需要再乘以DPI缩放率
        width = self.screen_width
        height = self.screen_height
        capture_scale = 1.0
        return width, height, capture_scale

    def window_screen(self):
        """windows api 窗体截图方法，可后台截图，可被遮挡，不兼容部分窗口"""
        hwnd = self.hwd_num
        screen_width = self.screen_width
        screen_height = self.screen_height

        # 直接使用窗口尺寸进行截图，不做任何DPI缩放
        print(f"<br>窗口尺寸: {screen_width}x{screen_height}")

        # 返回句柄窗口的设备环境，覆盖整个窗口，包括非客户区，标题栏，菜单，边框
        hwnd_dc = GetWindowDC(hwnd)
        # 创建设备描述表
        mfc_dc = CreateDCFromHandle(hwnd_dc)
        # 创建内存设备描述表
        save_dc = mfc_dc.CreateCompatibleDC()
        # 创建位图对象准备保存图片
        save_bit_map = CreateBitmap()
        # 为bitmap开辟存储空间
        save_bit_map.CreateCompatibleBitmap(mfc_dc, screen_width, screen_height)
        # 将截图保存到saveBitMap中
        save_dc.SelectObject(save_bit_map)
        # 优先使用PrintWindow获取后台画面，失败再BitBlt
        print_window_success = False
        try:
            print_window_success = bool(
                windll.user32.PrintWindow(
                    hwnd, save_dc.GetSafeHdc(), PW_RENDERFULLCONTENT
                )
            )
        except Exception:
            print_window_success = False
        if not print_window_success:
            # 从窗口左上角开始截取整个窗口
            save_dc.BitBlt(
                (0, 0), (screen_width, screen_height), mfc_dc, (0, 0), SRCCOPY
            )

        # 保存图像
        signed_ints_array = save_bit_map.GetBitmapBits(True)
        im_opencv = frombuffer(signed_ints_array, dtype="uint8")
        im_opencv.shape = (screen_height, screen_width, 4)
        im_opencv = cv2.cvtColor(im_opencv, cv2.COLOR_BGRA2GRAY)

        print(f"<br>截图成功！尺寸: {im_opencv.shape[1]}x{im_opencv.shape[0]}")

        # 测试显示截图图片
        # cv2.namedWindow('scr_img')  # 命名窗口
        # cv2.imshow("scr_img", im_opencv)  # 显示
        # cv2.waitKey(0)
        # cv2.destroyAllWindows()

        # 内存释放
        DeleteObject(save_bit_map.GetHandle())
        save_dc.DeleteDC()
        mfc_dc.DeleteDC()
        return im_opencv


def get_screen_scale_rate():
    """获取缩放比例"""
    set_config = ReadConfigFile()  # 读取配置文件
    other_setting = set_config.read_config_other_setting()
    try:
        screen_scale_rate = float(other_setting[11])
    except (TypeError, ValueError):
        screen_scale_rate = 1.0
    return screen_scale_rate
