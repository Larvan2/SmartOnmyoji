# -*- coding: utf-8 -*-
# @Link    : https://github.com/aicezam/SmartOnmyoji
# @Version : Python3.7.6
# @MIT License Copyright (c) 2022 ACE

import time
from os.path import abspath, dirname
# from cv2 import cv2
import cv2


_WINDOW_COUNTER = 0


def _reset_cv2_windows():
    """重置所有OpenCV窗口"""
    try:
        cv2.destroyAllWindows()
        for _ in range(5):
            cv2.waitKey(1)
    except Exception:
        pass


def _next_window_name():
    global _WINDOW_COUNTER
    _WINDOW_COUNTER += 1
    return f'scr_img_{int(time.time() * 1000)}_{_WINDOW_COUNTER}'


class ImgProcess:
    """图像处理，传入的图片格式必须是cv2的格式"""

    def __init__(self):
        super(ImgProcess, self).__init__()

    @staticmethod
    def save_img(img, img_path_name=r'\screen_img\screen_pic.jpg'):
        """保存内存中cv2格式的图片为本地文件"""
        if img is None:
            print("<br>未获取到需要保存的图片！")
        else:
            file_path = abspath(dirname(__file__)) + img_path_name  # 截图的存储位置，程序路径里面
            cv2.imwrite(file_path, img, [int(cv2.IMWRITE_JPEG_QUALITY), 20])  # 保存截图 质量（0-100）

    @staticmethod
    def show_img(img):
        """查看内存中cv2格式的图片"""
        if img is None:
            print("<br>未获取到需要显示的图片！")
            return
        
        try:
            # 先清理可能存在的死锁窗口
            _reset_cv2_windows()
            
            window_name = _next_window_name()
            print(f"<br>显示调试图片窗口: {window_name}")
            
            # 获取图片实际尺寸
            img_height, img_width = img.shape[:2]
            
            # 根据图片比例计算合适的显示尺寸（最大宽度1200，最大高度900）
            max_width = 1200
            max_height = 900
            
            # 计算缩放比例，保持原始宽高比
            scale_w = max_width / img_width
            scale_h = max_height / img_height
            scale = min(scale_w, scale_h, 1.0)  # 不放大，只缩小
            
            display_width = int(img_width * scale)
            display_height = int(img_height * scale)
            
            print(f"<br>图片原始尺寸: {img_width}x{img_height}, 显示尺寸: {display_width}x{display_height}")
            
            # 不使用startWindowThread，直接创建窗口
            cv2.namedWindow(window_name, cv2.WINDOW_NORMAL | cv2.WINDOW_KEEPRATIO)
            cv2.resizeWindow(window_name, display_width, display_height)
            
            print("<br>正在显示图片...")
            cv2.imshow(window_name, img)
            
            # 多次刷新确保窗口显示
            for _ in range(10):
                cv2.waitKey(10)
            
            print("<br>等待关闭窗口或按任意键继续...")
            
            # 等待用户关闭窗口或按键
            timeout_counter = 0
            max_timeout = 3000  # 5分钟超时 (3000 * 100ms)
            
            while timeout_counter < max_timeout:
                key = cv2.waitKey(100)
                timeout_counter += 1
                
                if key != -1 and key != 255:  # 用户按了键（排除-1和255）
                    print(f"<br>检测到按键: {key}，关闭窗口")
                    break
                
                try:
                    visible = cv2.getWindowProperty(window_name, cv2.WND_PROP_VISIBLE)
                    if visible < 1:  # 窗口被关闭
                        print("<br>窗口已关闭")
                        break
                except (cv2.error, Exception):
                    # 窗口已被销毁
                    print("<br>窗口已销毁")
                    break
            
            if timeout_counter >= max_timeout:
                print("<br>等待超时，自动关闭窗口")
            
            # 清理窗口
            try:
                cv2.destroyWindow(window_name)
                for _ in range(5):
                    cv2.waitKey(1)
                print("<br>窗口已清理")
            except (cv2.error, Exception) as e:
                print(f"<br>清理窗口时出错: {e}")
                
        except Exception as e:
            print(f"<br>显示图片时出错: {e}")
            import traceback
            print(f"<br>详细错误: {traceback.format_exc()}")

    @staticmethod
    def draw_pos_in_img(img, pos, height_width):
        """
        在图片中指定坐标点绘制边框
        :param img: 需要绘制边框的图片
        :param pos: 中心坐标点
        :param height_width: 要绘制的边框的高和宽
        :return: 返回坐标(x,y) 与opencv坐标系对应
        """
        if pos is None:
            print("<br>未获取坐标点位置！")
        else:
            img = cv2.rectangle(img,
                                (pos[0] - int(height_width[1] * 0.5), pos[1] - int(height_width[0] * 0.5)),
                                (pos[0] + int(height_width[1] * 0.5), pos[1] + int(height_width[0] * 0.5)),
                                (0, 238, 118),
                                2)  # 参数解释：图片，左上角坐标，右下角坐标，颜色，线宽
            return img

    @staticmethod
    def img_compress(img, compress_val=0.5):
        """压缩图片，默认0.5倍"""
        height, width = img.shape[:2]  # 获取宽高
        # 压缩图片,压缩率compress_val
        size = (int(width * compress_val), int(height * compress_val))
        img = cv2.resize(img, size, interpolation=cv2.INTER_AREA)
        return img

    @staticmethod
    def get_sift(img):
        """
        :param img: 传入cv2格式的图片，获取特征点信息
        :return: 返回特征点信息
        """
        # 初始化SIFT探测器
        sift = cv2.SIFT_create()
        # cv.xfeatures2d.BEBLID_create(0.75)  # 已过时用法
        kp, des = sift.detectAndCompute(img, None)
        img_sift = [kp, des]
        return img_sift
