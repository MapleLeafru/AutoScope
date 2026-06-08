# -*- coding: utf-8 -*-
import sys
import os
import json

# Добавляем корень проекта в PYTHONPATH, чтобы импорты работали при запуске из C#.
ROOT_DIR = os.path.abspath(os.path.join(os.path.dirname(__file__), ".."))
sys.path.append(ROOT_DIR)

from Api.InputApi import InputApi
from Core.Core import Core
from Utils.Logger import Logger
from Utils.ConfigLoader import ConfigLoader
from Utils.ModuleRunner import ModuleRunner

sys.stdout.reconfigure(encoding="utf-8")


class PipelineContext:
    # Хранит данные и сервисы одного запуска InputPipeline.

    def __init__(self, request):
        self.request = request
        self.data = None
        self.logger = None

    def set_data(self, data):
        # Сохраняет промежуточные данные текущего этапа.
        self.data = data


class ParserAdapter:
    # Передаёт запуск внешнего парсера в общий ModuleRunner.

    @staticmethod
    def run(parser_config, context):
        # Запускает парсер и возвращает JSON-батчи из stdout.
        if not parser_config:
            raise RuntimeError("Parser config is missing")

        yield from ModuleRunner.run_streaming(
            module_config=parser_config,
            request=context.request,
            logger=context.logger,
            stage="PARSER",
        )


class InputPipelineManager:
    # Управляет цепочкой Parser -> InputApi -> Core -> SQLite.

    def __init__(self, context):
        self.context = context

        config_path = context.request.get("configPath")
        db_path = context.request.get("dbPath")

        db_config = ConfigLoader.load_database_config(db_path)
        brand_map = ConfigLoader.load_brand_country_map(config_path)
        api_settings = context.request.get("apiSettings", {})

        self.api = InputApi(db_config, brand_map, api_settings)
        self.core = Core(db_config)

        self.logger = Logger("input", context.request)
        self.context.logger = self.logger
        self.logger.info("PIPELINE", "=== Input Pipeline started ===")

    def run(self):
        # Запускает потоковую обработку и возвращает итоговый статус.
        try:
            self._run_streaming()
            self.logger.info("PIPELINE", "=== Input Pipeline finished ===")
            return {"status": "success"}

        except Exception as e:
            try:
                self.logger.error("PIPELINE", f"Fatal error: {str(e)}")
            except Exception:
                print("LOGGER FAILED:", str(e))

            return {
                "status": "error",
                "error": str(e),
            }

    def _run_streaming(self):
        # Построчно принимает batch от парсера, обрабатывает API и сохраняет в Core.
        parser_config = self.context.request.get("parser")
        db_path = self.context.request.get("dbPath")

        self.logger.info("PIPELINE", "Streaming pipeline started")

        total_received = 0
        total_saved = 0
        total_skipped = 0

        for batch in ParserAdapter.run(parser_config, self.context):
            if not batch:
                continue

            batch_size = len(batch) if isinstance(batch, list) else 1
            total_received += batch_size
            self.logger.info("PARSER", f"Received batch: {batch_size}")

            result = self.api.process(batch)
            data = result.get("data")
            meta = result.get("meta", {})

            skipped = meta.get("skipped", 0)
            total_skipped += skipped

            if skipped > 0:
                self.logger.warning("API", f"Skipped items: {skipped}")

            if not data:
                self.logger.warning("API", "Batch contains no valid data after API processing")
                continue

            try:
                results = self.core.save(data, db_path)
                total_saved += len(results)

                for item in results:
                    self.logger.info(
                        "CORE",
                        f"ads_id={item['ads_id']}, "
                        f"check_id={item['check_id']}, "
                        f"has_changes={item['has_changes']}",
                    )

            except Exception as e:
                self.logger.error("CORE", f"DB error: {str(e)}")
                raise

        self.logger.info(
            "PIPELINE",
            f"Streaming pipeline finished. received={total_received}, "
            f"saved={total_saved}, skipped={total_skipped}",
        )


# Точка входа InputPipelineManager при запуске из C#.
def main():
    try:
        input_json = sys.stdin.read()

        if not input_json:
            raise RuntimeError("InputPipelineManager received empty input")

        request = json.loads(input_json)

        context = PipelineContext(request)
        pipeline = InputPipelineManager(context)
        result = pipeline.run()

        print(json.dumps(result, ensure_ascii=False))

    except Exception as e:
        print(json.dumps({"status": "fatal_error", "error": str(e)}, ensure_ascii=False))


if __name__ == "__main__":
    main()
