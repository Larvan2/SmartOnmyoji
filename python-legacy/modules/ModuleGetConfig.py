# -*- coding: utf-8 -*-
# @Link    : https://github.com/aicezam/SmartOnmyoji
# @Version : Python3.7.6
# @MIT License Copyright (c) 2022 ACE

from configparser import ConfigParser
from dataclasses import dataclass
from typing import Any, Iterable, Iterator, List


@dataclass
class UIInfo:
    """Strongly-typed UI configuration container with sequence (list-like) compatibility.

    This preserves backward compatibility with older code that treats ui_info as a list
    (e.g. info[0], info[3]) while providing attribute access (ui.connect_mod) which is
    much clearer and less error prone.
    """
    connect_mod: str
    target_path_mode: str
    handle_title: str
    click_deviation: int
    interval_seconds: float
    loop_min: float
    img_compress_val: float
    match_method: str
    run_mode: str
    custom_target_path: str
    process_num: str
    handle_num: Any
    if_end: str
    debug_status: bool
    set_priority_status: bool
    interval_seconds_max: float
    screen_scale_rate: Any
    times_mode: str

    def to_list(self) -> List[Any]:
        return [
            self.connect_mod,
            self.target_path_mode,
            self.handle_title,
            self.click_deviation,
            self.interval_seconds,
            self.loop_min,
            self.img_compress_val,
            self.match_method,
            self.run_mode,
            self.custom_target_path,
            self.process_num,
            self.handle_num,
            self.if_end,
            self.debug_status,
            self.set_priority_status,
            self.interval_seconds_max,
            self.screen_scale_rate,
            self.times_mode,
        ]

    # Make the object behave like a read-only sequence so old indexing code keeps working
    def __len__(self) -> int:
        return 18

    def __iter__(self) -> Iterator[Any]:
        return iter(self.to_list())

    def __getitem__(self, idx):
        return self.to_list()[idx]

    @classmethod
    def from_sequence(cls, seq: Iterable[Any]) -> "UIInfo":
        """Create a UIInfo from a sequence (list/tuple). Fill missing fields with sensible defaults.

        This helps keep compatibility with older callers that may pass shorter lists.
        """
        seq_list = list(seq)

        # define defaults in the same order as to_list
        defaults = [
            "Windows程序窗体",
            "御魂",
            "",
            35,
            5.0,
            90.0,
            1.0,
            "模板匹配",
            "正常-可后台",
            "",
            "单开",
            "",
            "不执行任何操作",
            False,
            True,
            30.0,
            "1.0",
            "按分钟计算",
        ]

        # extend seq_list with defaults where missing
        for i in range(len(defaults)):
            if i >= len(seq_list):
                seq_list.append(defaults[i])

        # cast some numeric fields
        try:
            seq_list[3] = int(seq_list[3])
        except Exception:
            seq_list[3] = defaults[3]

        try:
            seq_list[4] = float(seq_list[4])
        except Exception:
            seq_list[4] = defaults[4]

        try:
            seq_list[5] = float(seq_list[5])
        except Exception:
            seq_list[5] = defaults[5]

        try:
            seq_list[6] = float(seq_list[6])
        except Exception:
            seq_list[6] = defaults[6]

        try:
            seq_list[15] = float(seq_list[15])
        except Exception:
            seq_list[15] = defaults[15]

        return cls(*seq_list[:18])

from os.path import abspath, dirname, exists


class ReadConfigFile:
    def __init__(self):
        super(ReadConfigFile, self).__init__()
        self.file_path = abspath(dirname(dirname(__file__))) + r'/modules/config.ini'  # 获取配置文件的绝对路径

    def read_config_ui_info(self):
        config_ini = ConfigParser()

        # 校验文件是否存在
        if not exists(self.file_path):
            raise FileNotFoundError("配置文件不存在！")

        config_ini.read(self.file_path, encoding="utf-8-sig")  # 读配置文件

        # 读取confing.ini的参数
        connect_mod = config_ini.get('ui_info', 'connect_mod')
        target_path_mode = config_ini.get('ui_info', 'target_path_mode')
        handle_title = config_ini.get('ui_info', 'handle_title')
        click_deviation = int(config_ini.get('ui_info', 'click_deviation'))
        interval_seconds = float(config_ini.get('ui_info', 'interval_seconds'))
        loop_min = float(config_ini.get('ui_info', 'loop_min'))
        img_compress_val = float(config_ini.get('ui_info', 'img_compress_val'))
        match_method = config_ini.get('ui_info', 'match_method')
        run_mode = config_ini.get('ui_info', 'run_mode')
        custom_target_path = config_ini.get('ui_info', 'custom_target_path')
        process_num = config_ini.get('ui_info', 'process_num')
        handle_num = config_ini.get('ui_info', 'handle_num')
        if_end = config_ini.get('ui_info', 'if_end')
        debug_status = self.str_to_bool(config_ini.get('ui_info', 'debug_status'))
        set_priority_status = self.str_to_bool(config_ini.get('ui_info', 'set_priority_status'))
        interval_seconds_max = float(config_ini.get('ui_info', 'interval_seconds_max'))
        screen_scale_rate = config_ini.get('other_setting', 'screen_scale_rate')
        times_mode = config_ini.get('ui_info', 'times_mode')

        # Prefer returning a UIInfo object with named attributes but keep sequence behaviour
        ui = UIInfo(
            connect_mod=connect_mod,
            target_path_mode=target_path_mode,
            handle_title=handle_title,
            click_deviation=click_deviation,
            interval_seconds=interval_seconds,
            loop_min=loop_min,
            img_compress_val=img_compress_val,
            match_method=match_method,
            run_mode=run_mode,
            custom_target_path=custom_target_path,
            process_num=process_num,
            handle_num=handle_num,
            if_end=if_end,
            debug_status=debug_status,
            set_priority_status=set_priority_status,
            interval_seconds_max=interval_seconds_max,
            screen_scale_rate=screen_scale_rate,
            times_mode=times_mode,
        )

        return ui

    def read_config_target_path_files_name(self):
        config_ini = ConfigParser()

        # 校验文件是否存在
        if not exists(self.file_path):
            raise FileNotFoundError("配置文件不存在！")

        config_ini.read(self.file_path, encoding="utf-8-sig")  # 读配置文件

        # 动态读取所有 file_name_* 配置项
        target_file_name_list = []
        try:
            # 首先尝试读取所有 file_name_* 项
            if config_ini.has_section('target_path_files_name'):
                for option in config_ini.options('target_path_files_name'):
                    if option.startswith('file_name_'):
                        file_content = config_ini.get('target_path_files_name', option)
                        # 分割中文名称和文件夹名称
                        parts = [p.strip() for p in file_content.split(",")]
                        if len(parts) == 2:
                            target_file_name_list.append(parts)
        except Exception as e:
            print(f"读取目标配置时出错: {e}")
        
        # 如果没有读到任何配置，返回空列表
        if not target_file_name_list:
            target_file_name_list = [
                ['御魂', 'yuhun'],
                ['探索', 'tansuo'],
                ['突破', 'tupo'],
                ['活动', 'huodong'],
                ['觉醒', 'juexing'],
                ['百鬼夜行', 'baigui'],
                ['御灵', 'yuling'],
            ]

        return target_file_name_list

    def read_config_other_setting(self):
        config_ini = ConfigParser()

        # 校验文件是否存在
        if not exists(self.file_path):
            raise FileNotFoundError("配置文件不存在！")

        config_ini.read(self.file_path, encoding="utf-8-sig")  # 读配置文件

        # 读取confing.ini的参数
        save_ui_info_in_config = self.str_to_bool(config_ini.get('other_setting', 'save_ui_info_in_config'))
        playtime_warming_status = self.str_to_bool(config_ini.get('other_setting', 'playtime_warming_status'))
        success_times_warming_status = self.str_to_bool(config_ini.get('other_setting', 'success_times_warming_status'))
        success_times_warming_times = config_ini.get('other_setting', 'success_times_warming_times')
        success_times_warming_waiting_seconds = config_ini.get('other_setting', 'success_times_warming_waiting_seconds')
        debug_status_show_pics = self.str_to_bool(config_ini.get('other_setting', 'debug_status_show_pics'))
        set_priority_num = config_ini.get('other_setting', 'set_priority_num')
        play_sound_status = self.str_to_bool(config_ini.get('other_setting', 'play_sound_status'))
        adb_wifi_status = self.str_to_bool(config_ini.get('other_setting', 'adb_wifi_status'))
        adb_wifi_ip = config_ini.get('other_setting', 'adb_wifi_ip')
        ex_click = config_ini.get('other_setting', 'ex_click')
        screen_scale_rate = config_ini.get('other_setting', 'screen_scale_rate')
        if_match_then_stop = self.str_to_bool(config_ini.get('other_setting', 'if_match_then_stop'))
        stop_target_img_name = config_ini.get('other_setting', 'stop_target_img_name')
        if_match_5times_stop = self.str_to_bool(config_ini.get('other_setting', 'if_match_5times_stop'))
        save_click_log = self.str_to_bool(config_ini.get('other_setting', 'save_click_log'))
        target_deviation = int(config_ini.get('other_setting', 'target_deviation'))
        success_match_then_wait = config_ini.get('other_setting', 'success_match_then_wait')

        other_setting = [save_ui_info_in_config, playtime_warming_status, success_times_warming_status,
                         success_times_warming_times, success_times_warming_waiting_seconds.split(","),
                         debug_status_show_pics, set_priority_num, play_sound_status, adb_wifi_status, adb_wifi_ip,
                         ex_click, screen_scale_rate, if_match_then_stop, stop_target_img_name.split(","),
                         if_match_5times_stop, save_click_log, target_deviation, success_match_then_wait.split(",")]

        return other_setting

    def writ_config_ui_info(self, info):
        config_ini = ConfigParser(comment_prefixes='/', allow_no_value=True)  # 保留注释

        # 校验文件是否存在
        if not exists(self.file_path):
            raise FileNotFoundError("配置文件不存在！")

        # Accept either a UIInfo instance or a sequence/list for backward compatibility
        if isinstance(info, UIInfo):
            info_list = info.to_list()
        else:
            # the caller may pass a list-like object
            info_list = list(info)

        # 先把所有参数转为str格式，否则写入会报错
        for i in range(len(info_list)):
            info_list[i] = str(info_list[i])

        config_ini.read(self.file_path, encoding="utf-8-sig")  # 读配置文件

        # 写入confing.ini的参数
        config_ini.set("ui_info", "connect_mod", info_list[0])
        config_ini.set("ui_info", "target_path_mode", info_list[1])
        config_ini.set("ui_info", "handle_title", info_list[2])
        config_ini.set("ui_info", "click_deviation", info_list[3])
        config_ini.set("ui_info", "interval_seconds", info_list[4])
        config_ini.set("ui_info", "loop_min", info_list[5])
        config_ini.set("ui_info", "img_compress_val", info_list[6])
        config_ini.set("ui_info", "match_method", info_list[7])
        config_ini.set("ui_info", "run_mode", info_list[8])
        config_ini.set("ui_info", "custom_target_path", info_list[9])
        config_ini.set("ui_info", "process_num", info_list[10])
        config_ini.set("ui_info", "handle_num", info_list[11])
        config_ini.set("ui_info", "if_end", info_list[12])
        config_ini.set("ui_info", "debug_status", info_list[13])
        config_ini.set("ui_info", "set_priority_status", info_list[14])
        config_ini.set("ui_info", "interval_seconds_max", info_list[15])
        config_ini.set("other_setting", "screen_scale_rate", info_list[16])
        config_ini.set("ui_info", "times_mode", info_list[17])

        # 写入文件
        config_ini.write(open(self.file_path, 'w', encoding="utf-8"))

    def write_config_target_path_files_name(self, target_file_name_list):
        """写入目标列表到配置文件"""
        config_ini = ConfigParser(comment_prefixes='/', allow_no_value=True)
        
        if not exists(self.file_path):
            raise FileNotFoundError("配置文件不存在！")
        
        config_ini.read(self.file_path, encoding="utf-8-sig")
        
        # 确保 target_path_files_name 段存在
        if not config_ini.has_section('target_path_files_name'):
            config_ini.add_section('target_path_files_name')
        
        # 移除旧的所有 file_name_* 项
        for option in list(config_ini.options('target_path_files_name')):
            if option.startswith('file_name_'):
                config_ini.remove_option('target_path_files_name', option)
        
        # 写入新的目标列表
        for i, target_info in enumerate(target_file_name_list):
            if len(target_info) >= 2:
                file_content = f"{target_info[0]},{target_info[1]}"
                config_ini.set('target_path_files_name', f'file_name_{i}', file_content)
        
        # 写入文件
        with open(self.file_path, 'w', encoding="utf-8") as f:
            config_ini.write(f)

    @staticmethod
    def str_to_bool(str_val):
        return True if str_val.lower() == 'true' else False

# rc = ReadConfigFile()  # 实例化
#
# info_r = rc.read_config_ui_info()  # 读参数
# print(info_r)
#
# # info_w = ['0', '1', '2', '3', '4', '5', '6', '7', '8', '9', '10', '11', '12', '13', '14']
# info_w = ['Windows程序窗体', '御魂', '阴阳师-网易游戏', 35, 5.0, 90.0, 1.0, '模板匹配', '正常-可后台', None, '单开', 0, '不执行任何操作', False, True]
#
# rc.writ_config_ui_info(info_w)  # 写参数


# rc = ReadConfigFile()
# file_name = rc.read_config_target_path_files_name()
# print(file_name)
