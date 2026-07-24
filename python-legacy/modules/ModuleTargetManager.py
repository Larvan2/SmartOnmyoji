# -*- coding: utf-8 -*-
"""目标管理对话框 - 允许用户添加、编辑、删除目标"""

from PyQt5.QtWidgets import (QDialog, QVBoxLayout, QHBoxLayout, QTableWidget, 
                             QTableWidgetItem, QPushButton, QLineEdit, QLabel,
                             QMessageBox, QHeaderView)
from PyQt5.QtCore import Qt
from modules.ModuleGetConfig import ReadConfigFile


class TargetManagerDialog(QDialog):
    """目标管理对话框"""
    
    def __init__(self, parent=None, target_list=None):
        super().__init__(parent)
        self.setWindowTitle("目标管理")
        self.setGeometry(200, 200, 600, 400)
        self.target_list = target_list if target_list else []
        self.config = ReadConfigFile()
        self.init_ui()
        self.load_targets()
    
    def init_ui(self):
        """初始化界面"""
        layout = QVBoxLayout()
        
        # 表格显示现有目标
        self.table = QTableWidget()
        self.table.setColumnCount(2)
        self.table.setHorizontalHeaderLabels(["目标名称（中文）", "文件夹名称（英文）"])
        self.table.horizontalHeader().setSectionResizeMode(0, QHeaderView.Stretch)
        self.table.horizontalHeader().setSectionResizeMode(1, QHeaderView.Stretch)
        layout.addWidget(self.table)
        
        # 输入框和按钮
        input_layout = QHBoxLayout()
        
        input_layout.addWidget(QLabel("目标名称:"))
        self.name_input = QLineEdit()
        self.name_input.setPlaceholderText("例如: 御魂")
        input_layout.addWidget(self.name_input)
        
        input_layout.addWidget(QLabel("文件夹:"))
        self.folder_input = QLineEdit()
        self.folder_input.setPlaceholderText("例如: yuhun")
        input_layout.addWidget(self.folder_input)
        
        self.add_btn = QPushButton("添加目标")
        self.add_btn.clicked.connect(self.add_target)
        input_layout.addWidget(self.add_btn)
        
        layout.addLayout(input_layout)
        
        # 操作按钮
        button_layout = QHBoxLayout()
        
        self.delete_btn = QPushButton("删除选中")
        self.delete_btn.clicked.connect(self.delete_target)
        button_layout.addWidget(self.delete_btn)
        
        button_layout.addStretch()
        
        self.save_btn = QPushButton("保存")
        self.save_btn.clicked.connect(self.save_targets)
        button_layout.addWidget(self.save_btn)
        
        self.cancel_btn = QPushButton("取消")
        self.cancel_btn.clicked.connect(self.reject)
        button_layout.addWidget(self.cancel_btn)
        
        layout.addLayout(button_layout)
        
        self.setLayout(layout)
    
    def load_targets(self):
        """从配置文件加载目标列表"""
        try:
            self.target_list = self.config.read_config_target_path_files_name()
        except Exception as e:
            QMessageBox.warning(self, "错误", f"加载目标列表失败: {e}")
        
        self.refresh_table()
    
    def refresh_table(self):
        """刷新表格显示"""
        self.table.setRowCount(0)
        
        for i, target_info in enumerate(self.target_list):
            self.table.insertRow(i)
            
            # 目标名称
            name_item = QTableWidgetItem(target_info[0] if len(target_info) > 0 else "")
            self.table.setItem(i, 0, name_item)
            
            # 文件夹名称
            folder_item = QTableWidgetItem(target_info[1] if len(target_info) > 1 else "")
            self.table.setItem(i, 1, folder_item)
    
    def add_target(self):
        """添加新目标"""
        name = self.name_input.text().strip()
        folder = self.folder_input.text().strip()
        
        if not name or not folder:
            QMessageBox.warning(self, "警告", "请填写完整的目标名称和文件夹名称")
            return
        
        # 检查是否重复
        for target in self.target_list:
            if target[0] == name:
                QMessageBox.warning(self, "警告", f"目标 '{name}' 已经存在")
                return
        
        # 添加到列表
        self.target_list.append([name, folder])
        self.name_input.clear()
        self.folder_input.clear()
        self.refresh_table()
        QMessageBox.information(self, "成功", "目标已添加，点击'保存'按钮后生效")
    
    def delete_target(self):
        """删除选中的目标"""
        current_row = self.table.currentRow()
        
        if current_row < 0:
            QMessageBox.warning(self, "警告", "请选择要删除的目标")
            return
        
        target_name = self.table.item(current_row, 0).text()
        reply = QMessageBox.question(self, "确认", f"确定要删除 '{target_name}' 吗?",
                                     QMessageBox.Yes | QMessageBox.No)
        
        if reply == QMessageBox.Yes:
            self.target_list.pop(current_row)
            self.refresh_table()
    
    def save_targets(self):
        """保存目标列表到配置文件"""
        try:
            # 从表格读取最新数据
            updated_list = []
            for i in range(self.table.rowCount()):
                name = self.table.item(i, 0).text().strip()
                folder = self.table.item(i, 1).text().strip()
                
                if not name or not folder:
                    QMessageBox.warning(self, "警告", f"第 {i+1} 行数据不完整，请填写完整")
                    return
                
                updated_list.append([name, folder])
            
            # 写入配置文件
            self.config.write_config_target_path_files_name(updated_list)
            QMessageBox.information(self, "成功", "目标列表已保存！\n请重新启动脚本以加载新目标。")
            self.accept()
        except Exception as e:
            QMessageBox.critical(self, "错误", f"保存失败: {e}")
