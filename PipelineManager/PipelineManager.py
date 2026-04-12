
print("Hello world")

import sys
import json
import subprocess
import traceback
from typing import Any, Dict


# =========================================================
# Context (хранит состояние pipeline)
# =========================================================

class PipelineContext:
    def __init__(self, request: Dict[str, Any]):
        self.request = request
        self.data = None          # данные между этапами
        self.errors = []
        self.meta = {}

    def set_data(self, data: Dict[str, Any]):
        self.data = data

    def add_error(self, error: str):
        self.errors.append(error)

    def has_errors(self) -> bool:
        return len(self.errors) > 0


# =========================================================
# Parser Adapter (универсальный запуск парсеров)
# =========================================================

class ParserAdapter:

    @staticmethod
    def run(parser_config: Dict[str, Any], context: PipelineContext) -> Dict[str, Any]:
        parser_type = parser_config.get("type")

        if parser_type == "python":
            return ParserAdapter._run_python(parser_config, context)

        elif parser_type == "executable":
            return ParserAdapter._run_executable(parser_config, context)

        elif parser_type == "command":
            return ParserAdapter._run_command(parser_config, context)

        else:
            raise Exception(f"Unknown parser type: {parser_type}")

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

        if error:
            raise Exception(f"Parser error: {error}")

        return json.loads(output) if output else {}

    @staticmethod
    def _run_executable(parser_config, context):
        path = parser_config.get("path")

        process = subprocess.Popen(
            [path],
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

        if error:
            raise Exception(f"Parser error: {error}")

        return json.loads(output) if output else {}

    @staticmethod
    def _run_command(parser_config, context):
        cmd = parser_config.get("cmd")

        process = subprocess.Popen(
            cmd,
            stdin=subprocess.PIPE,
            stdout=subprocess.PIPE,
            stderr=subprocess.PIPE,
            text=True,
            shell=True
        )

        process.stdin.write(json.dumps(context.request))
        process.stdin.close()

        output = process.stdout.read()
        error = process.stderr.read()

        process.wait()

        if error:
            raise Exception(f"Parser error: {error}")

        return json.loads(output) if output else {}


# =========================================================
# Заглушки (ты заменишь своими реализациями)
# =========================================================

class Api:
    def process(self, data: Dict[str, Any]) -> Dict[str, Any]:
        # TODO: заменить своей логикой
        return data


class Core:
    def save(self, data: Dict[str, Any], db_path: str):
        # TODO: заменить своей логикой
        print(f"[CORE] Saving to DB: {db_path}")
        print(json.dumps(data, indent=2, ensure_ascii=False))


# =========================================================
# Pipeline Manager
# =========================================================

class PipelineManager:

    def __init__(self, context: PipelineContext):
        self.context = context
        self.api = Api()
        self.core = Core()

    def run(self):
        try:
            self._run_parser()
            self._run_api()
            self._run_core()

            return {
                "status": "success",
                "errors": self.context.errors
            }

        except Exception as e:
            self.context.add_error(str(e))
            self.context.add_error(traceback.format_exc())

            return {
                "status": "error",
                "errors": self.context.errors
            }

    # -------------------------

    def _run_parser(self):
        parser_config = self.context.request.get("parser")

        if not parser_config:
            raise Exception("Parser config not provided")

        print("[PIPELINE] Running parser...")

        data = ParserAdapter.run(parser_config, self.context)

        if not isinstance(data, dict):
            raise Exception("Parser must return JSON object")

        self.context.set_data(data)

    # -------------------------

    def _run_api(self):
        print("[PIPELINE] Running API...")

        processed = self.api.process(self.context.data)

        if processed is None:
            self.context.add_error("API returned None")

        self.context.set_data(processed)

    # -------------------------

    def _run_core(self):
        print("[PIPELINE] Running CORE...")

        db_path = self.context.request.get("dbPath")

        if not db_path:
            raise Exception("dbPath not provided")

        self.core.save(self.context.data, db_path)


# =========================================================
# Entry Point
# =========================================================

def main():
    try:
        input_json = sys.stdin.read()

        if not input_json:
            raise Exception("No input JSON provided")

        request = json.loads(input_json)

        context = PipelineContext(request)
        pipeline = PipelineManager(context)

        result = pipeline.run()

        print(json.dumps(result, ensure_ascii=False))

    except Exception as e:
        error_result = {
            "status": "fatal_error",
            "error": str(e),
            "trace": traceback.format_exc()
        }

        print(json.dumps(error_result, ensure_ascii=False))


if __name__ == "__main__":
    main()