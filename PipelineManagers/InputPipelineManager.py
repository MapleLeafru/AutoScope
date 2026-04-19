import sys
import os
import json
import subprocess

## import traceback

# Добавляем корень проекта в PYTHONPATH
ROOT_DIR = os.path.abspath(os.path.join(os.path.dirname(__file__), ".."))
sys.path.append(ROOT_DIR)

from Api.Api import Api
from Core.Core import Core

sys.stdout.reconfigure(encoding='utf-8')

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
# ↓                         #
#Core (внутри Python)       #
# ↓                         #
#SQLite                     #
#############################

# =========================================================
# Context
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


# =========================================================
# Parser Adapter
# =========================================================

class ParserAdapter:

    @staticmethod
    def run(parser_config, context):

        ext = os.path.splitext(parser_config.get("parserPath"))[1].lower()

        if ext == ".py":
            return ParserAdapter._run_python(parser_config, context)
        #elif ext == ".js":
        #    return ParserAdapter._run_node(...)
        #elif ext == "":

        raise Exception(f"Unsupported parser type: {parser_type}")


    @staticmethod
    def _run_python(parser_config, context):
        path = parser_config.get("parserPath")
        python_path = parser_config.get("python", "python")

        env = os.environ.copy()
        env["PYTHONIOENCODING"] = "utf-8"

        process = subprocess.Popen(
            [python_path, path],
            stdin=subprocess.PIPE,
            stdout=subprocess.PIPE,
            stderr=subprocess.PIPE,
            text=True,
            encoding="utf-8",
            errors="replace",
            env=env
        )

        # отправляем входные данные
        process.stdin.write(json.dumps(context.request, ensure_ascii=False))
        process.stdin.close()

        # читаем результат
        output = process.stdout.read()
        error = process.stderr.read()

        #print("[DEBUG RAW OUTPUT]")                                         # debag
        #print(output)                                                       # debag

        process.wait()

        if error:                                                           # debag
            print("[PARSER ERROR]")                                         # debag
            print(error)                                                    # debag

        ## if error:
        ##     raise Exception(f"Parser error: {error}")

        if not output:
            return None

        try:
            return json.loads(output) if output else None
        except:
            return None


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


    # =========================================================
    # STAGES
    # =========================================================

    def _run_parser(self):
        parser_config = self.context.request.get("parser")

        print("[PIPELINE] Running parser...")

        data = ParserAdapter.run(parser_config, self.context)

        #print(f"[DEBUG] Parser output: {type(data)} | {data}")                              # debag

        self.context.set_data(data)


    def _run_api(self):
        print("[PIPELINE] Running API...")

        data = self.context.data

        processed = self.api.process(data)

        #print(f"[DEBUG] API output: {type(processed)} | {processed}")                       # debag

        self.context.set_data(processed)


    def _run_core(self):
        print("[PIPELINE] Running CORE...")

        data = self.context.data
        db_path = self.context.request.get("dbPath")

        if not data:
            print("[PIPELINE] No data to save")
            return

        self.core.save(data, db_path)


# =========================================================
# Entry Point
# =========================================================

def main():
    # try:

        input_json = sys.stdin.read()
        request = json.loads(input_json)

        context = PipelineContext(request)
        pipeline = PipelineManager(context)

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