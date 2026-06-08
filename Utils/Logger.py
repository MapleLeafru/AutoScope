# -*- coding: utf-8 -*-
import os
import sys
from datetime import datetime
from Utils.ConfigLoader import ConfigLoader

sys.stdout.reconfigure(encoding="utf-8")


class Logger:
    # Пишет логи input/output pipeline в отдельный файл одного запуска.

    def __init__(self, pipeline_type: str, request: dict):
        self.pipeline_type = pipeline_type
        self.pipeline_id = datetime.now().strftime("%Y-%m-%d_%H-%M-%S")
        self.module_name = self._extract_module_name(request)

        base_dir = os.path.abspath(os.path.join(os.path.dirname(__file__), ".."))
        folder = os.path.join(base_dir, "Logs", pipeline_type)
        os.makedirs(folder, exist_ok=True)

        filename = f"{pipeline_type}_pipeline_{self.pipeline_id}.log"
        self.file_path = os.path.join(folder, filename)

        self.config = ConfigLoader.load_logger_config()
        self._cleanup_old_logs(folder)

    def _extract_module_name(self, request):
        # Определяет имя парсера или анализатора для контекста логов.
        parser = request.get("parser", {}) or {}
        analyzer = request.get("analyzer", {}) or {}

        module_path = (
            parser.get("modulePath")
            or parser.get("parserPath")
            or analyzer.get("modulePath")
            or analyzer.get("analyzerPath")
            or "unknown"
        )

        return os.path.basename(module_path)

    def _format_context(self):
        # Формирует общий контекст строки лога.
        return f"[{self.module_name}][pipeline={self.pipeline_id}]"

    def _write(self, level: str, stage: str, message: str):
        # Записывает одну строку лога в файл текущего pipeline.
        timestamp = datetime.now().strftime("%Y-%m-%d %H:%M:%S")
        context_str = self._format_context()
        log_line = f"{timestamp} | {level} | {stage} | {context_str} {message}\n"

        with open(self.file_path, "a", encoding="utf-8") as f:
            f.write(log_line)

    def info(self, stage: str, message: str):
        # Записывает информационное сообщение.
        self._write("INFO", stage, message)

    def warning(self, stage: str, message: str):
        # Записывает предупреждение.
        self._write("WARNING", stage, message)

    def error(self, stage: str, message: str):
        # Записывает ошибку.
        self._write("ERROR", stage, message)

    def debug(self, stage: str, message: str):
        # Записывает отладочное сообщение.
        self._write("DEBUG", stage, message)

    def _cleanup_old_logs(self, folder_path):
        # Удаляет старые логи по сроку хранения из LoggerConfig.json.
        retention_days = self.config.get("retention_days", 7)
        now = datetime.now()

        for filename in os.listdir(folder_path):
            file_path = os.path.join(folder_path, filename)

            if not os.path.isfile(file_path):
                continue

            try:
                file_time = datetime.fromtimestamp(os.path.getmtime(file_path))
                age_days = (now - file_time).days

                if age_days > retention_days:
                    os.remove(file_path)

            except Exception:
                # Ошибка очистки логов не должна ломать основной pipeline.
                pass
