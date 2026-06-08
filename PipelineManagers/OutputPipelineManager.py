# -*- coding: utf-8 -*-
import sys
import os
import json

# Добавляем корень проекта в PYTHONPATH, чтобы импорты работали при запуске из C#.
ROOT_DIR = os.path.abspath(os.path.join(os.path.dirname(__file__), ".."))
sys.path.append(ROOT_DIR)

from Api.OutputApi import OutputApi
from Core.Core import Core
from Utils.Logger import Logger
from Utils.ConfigLoader import ConfigLoader
from Utils.ModuleRunner import ModuleRunner

sys.stdout.reconfigure(encoding="utf-8")


class AnalyzerAdapter:
    # Передаёт запуск внешнего анализатора в общий ModuleRunner.

    @staticmethod
    def run(analyzer_config, data, context):
        # Запускает анализатор и возвращает его JSON-результат.
        if not analyzer_config:
            raise RuntimeError("Analyzer config is missing")

        return ModuleRunner.run_once(
            module_config=analyzer_config,
            input_payload=data,
            request=context.request,
            logger=context.logger,
            stage="ANALYZER"
        )


class OutputPipelineManager:
    # Управляет цепочкой: Core -> OutputApi -> Analyzer.

    def __init__(self, request):
        self.request = request

        db_path = request.get("dbPath")
        db_config = ConfigLoader.load_database_config(db_path)

        self.api = OutputApi(db_config)
        self.core = Core(db_config)

        self.logger = Logger("output", request)
        self.logger.info("PIPELINE", "=== Output Pipeline started ===")

    def run(self):
        # Получает данные из БД, готовит их через OutputApi и запускает анализатор.
        try:
            self.logger.info("API", "Preparing query")
            query = self.api.prepare(self.request)

            self.logger.info("CORE", "Fetching data")
            data = self.core.fetch_all(self.request.get("dbPath"))
            self.logger.info("CORE", f"Fetched records: {len(data)}")

            self.logger.info("API", "Processing data")
            result = self.api.process(data)

            analyzer_config = self.request.get("analyzer")

            if not analyzer_config:
                self.logger.warning("ANALYZER", "No analyzer provided")
                return {"status": "no_analyzer"}

            self.logger.info("ANALYZER", "Running analyzer")
            analyzer_result = AnalyzerAdapter.run(analyzer_config, result, self)

            self.logger.info("PIPELINE", "=== Output Pipeline finished ===")

            return {
                "status": "success",
                "result": analyzer_result
            }

        except Exception as e:
            self.logger.error("PIPELINE", f"Fatal error: {str(e)}")
            return {
                "status": "error",
                "error": str(e)
            }


def main():
    # Точка входа OutputPipelineManager при запуске из C#.
    try:
        input_json = sys.stdin.read()

        if not input_json:
            raise RuntimeError("OutputPipelineManager received empty input")

        request = json.loads(input_json)

        pipeline = OutputPipelineManager(request)
        result = pipeline.run()

        print(json.dumps(result, ensure_ascii=False))

    except Exception as e:
        print(json.dumps({"status": "fatal_error", "error": str(e)}, ensure_ascii=False))


if __name__ == "__main__":
    main()
