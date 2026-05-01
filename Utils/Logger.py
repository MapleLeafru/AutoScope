import os
import sys
from datetime import datetime
from Utils.ConfigLoader import ConfigLoader

sys.stdout.reconfigure(encoding='utf-8')

class Logger:

    def __init__(self, pipeline_type: str, request: dict):
        """
        pipeline_type: input / output
        request: JSON из C#
        """
        
        self.pipeline_type = pipeline_type

        # контекст
        self.pipeline_id = datetime.now().strftime("%Y-%m-%d_%H-%M-%S")
        #self.source = request.get("source", "unknown") # По идее можно просто удалить, но пускай пока поживет

        parser = request.get("parser", {})
        self.parser_name = os.path.basename(parser.get("parserPath", "unknown"))

        # папка
        BASE_DIR = os.path.abspath(
            os.path.join(os.path.dirname(__file__), "..")
        )
        folder = os.path.join(BASE_DIR, "Logs", pipeline_type)
        os.makedirs(folder, exist_ok=True)

        filename = f"{pipeline_type}_pipeline_{self.pipeline_id}.log"
        self.file_path = os.path.join(folder, filename)

        # загрузка конфига логгера
        self.config = ConfigLoader.load_logger_config()
        # очистка старых логов
        self._cleanup_old_logs(folder)

    # =========================================================
    # INTERNAL
    # =========================================================

    def _format_context(self):
        return f"[{self.parser_name}][pipeline={self.pipeline_id}]"

    def _write(self, level: str, stage: str, message: str):
        timestamp = datetime.now().strftime("%Y-%m-%d %H:%M:%S")

        context_str = self._format_context()

        log_line = f"{timestamp} | {level} | {stage} | {context_str} {message}\n"

        with open(self.file_path, "a", encoding="utf-8") as f:
            f.write(log_line)

    # =========================================================
    # LEVELS
    # =========================================================

    def info(self, stage: str, message: str):
        self._write("INFO", stage, message)

    def warning(self, stage: str, message: str):
        self._write("WARNING", stage, message)

    def error(self, stage: str, message: str):
        self._write("ERROR", stage, message)

    def debug(self, stage: str, message: str):
        self._write("DEBUG", stage, message)

    # =========================================================
    # CLEANUP OLD LOGS
    # =========================================================

    def _cleanup_old_logs(self, folder_path):

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

            except Exception as e:
                # не валим пайплайн из-за логов
                pass