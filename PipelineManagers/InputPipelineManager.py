import sys
import os
import json
import subprocess
## import traceback
## from typing import Any, Dict

# Добавляем корень проекта в PYTHONPATH
ROOT_DIR = os.path.abspath(os.path.join(os.path.dirname(__file__), ".."))
sys.path.append(ROOT_DIR)
from Api.Api import Api
from Core.Core import Core

#############################
#C#                         #
# ↓ (stdin JSON)            #
#PipelineManager            #
# ↓                         #
#Parser (отдельный процесс) #
# ↓ (stdout JSON)           #
#PipelineManager            #
# ↓                         #
#Api (внутри Python)        #
# ↓                         # !!!! 
#Core (внутри Python)       #
# ↓                         #
#SQLite                     #
#############################

# - Исключения и всё что с этим связано
## - Всё остальное второстепенное с обработкой ошибок

sys.stdout.reconfigure(encoding='utf-8')


# =========================================================
# Context (хранит состояние pipeline)
# =========================================================

class PipelineContext:
    def __init__(self, request):
        self.request = request
        self.data = None
        ## self.errors = []
        ## self.meta = {}

    def set_data(self, data):
        self.data = data

    ## def add_error(self, error):
    ##     self.errors.append(error)

    ## def has_errors(self):
    ##     return len(self.errors) > 0


# =========================================================
# Parser Adapter (универсальный запуск парсеров)
# =========================================================

class ParserAdapter:

    @staticmethod
    def run(parser_config, context):
        return ParserAdapter._run_python(parser_config, context)

    @staticmethod
    def _run_python(parser_config, context):
        path = parser_config.get("path")

        process = subprocess.Popen(
            ["python", path],
            stdin=subprocess.PIPE,
            stdout=subprocess.PIPE,
            stderr=subprocess.PIPE,
            text=True
        )

        process.stdin.write(json.dumps(context.request))
        process.stdin.close()

        output = process.stdout.read()
        error = process.stderr.read()

        process.wait()

        ## if error:
        ##     raise Exception(f"Parser error: {error}")

        return json.loads(output) if output else {}


# =========================================================
# Pipeline Manager
# =========================================================

class PipelineManager:

    def __init__(self, context):
        self.context = context

        config_path = context.request.get("configPath")
        self.api = Api(config_path)
        self.core = Core()

    def run(self):
        # try:
            self._run_parser()
            self._run_api()
            self._run_core()

            return {
                "status": "success"
                ## "errors": self.context.errors
            }

        # except Exception as e:
        #     self.context.add_error(str(e))
        #     self.context.add_error(traceback.format_exc())

        #     return {
        #         "status": "error",
        #         "errors": self.context.errors
        #     }

    # -------------------------

    def _run_parser(self):
        parser_config = self.context.request.get("parser")

        print("[PIPELINE] Running parser...")

        data = ParserAdapter.run(parser_config, self.context)

        self.context.set_data(data)

    # -------------------------

    def _run_api(self):
        print("[PIPELINE] Running API...")

        processed = self.api.process(self.context.data)

        self.context.set_data(processed)

    # -------------------------

    def _run_core(self):
        print("[PIPELINE] Running CORE...")

        db_path = self.context.request.get("dbPath")

        self.core.save(self.context.data, db_path)


# =========================================================
# Entry Point
# =========================================================

def main():
    # try:
        input_json = sys.stdin.read()

        request = json.loads(input_json)                # Загружаем json с параметрами

        context = PipelineContext(request)              # Context (хранит состояние pipeline) живёт весь pipeline
        pipeline = PipelineManager(context)             # создаём переменную класса Pipeline Manager

        result = pipeline.run()

        print(json.dumps(result, ensure_ascii=False))

    # except Exception as e:
    #     error_result = {
    #         "status": "fatal_error",
    #         "error": str(e),
    #         "trace": traceback.format_exc()
    #     }

    #     print(json.dumps(error_result, ensure_ascii=False))


if __name__ == "__main__":
    main()