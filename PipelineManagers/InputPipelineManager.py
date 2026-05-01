import sys
import os
import json
import subprocess

## import traceback

# Добавляем корень проекта в PYTHONPATH
ROOT_DIR = os.path.abspath(os.path.join(os.path.dirname(__file__), ".."))
sys.path.append(ROOT_DIR)

from Api.InputApi import InputApi
from Core.Core import Core
from Utils.Logger import Logger
from Utils.ConfigLoader import ConfigLoader

sys.stdout.reconfigure(encoding='utf-8')

#############################
#C#                         #
# ↓ (stdin JSON)            #
#InputPipelineManager            #
# ↓                         #
#Parser (отдельный процесс) #
# ↓ (stdout JSON)           #
#InputPipelineManager            #
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

        raise Exception(f"Unsupported parser type: {ext}")


    @staticmethod
    def _run_python(parser_config, context):
        path = parser_config.get("parserPath")
        python_path = parser_config.get("python", "python")

        env = os.environ.copy()
        env["PYTHONIOENCODING"] = "utf-8"

        if not os.path.exists(path):
            if context.logger:
                context.logger.error("PARSER", f"Parser file not found: {path}")
            return None

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

        if error:
            context_logger = getattr(context, "logger", None)
            if context_logger:
                context_logger.error("PARSER", f"stderr: {error.strip()}")

        #if error:                                                           # debag
        #    print("[PARSER ERROR]")                                         # debag
        #    print(error)                                                    # debag

        ## if error:
        ##     raise Exception(f"Parser error: {error}")

        if not output:
            if context.logger:
                context.logger.error("PARSER", "Parser returned empty output")
            return None

        try:
            return json.loads(output)
        except Exception as e:
            if context.logger:
                context.logger.error("PARSER", f"JSON parse error: {str(e)}")
                context.logger.error("PARSER", f"Raw output: {output[:500]}")
            return None


# =========================================================
# InputPipeline Manager
# =========================================================

class InputPipelineManager:

    def __init__(self, context):
        self.context = context

        config_path = context.request.get("configPath")
        db_path = context.request.get("dbPath")

        db_config = ConfigLoader.load_database_config(db_path)
        brand_map = ConfigLoader.load_brand_country_map(config_path)

        self.api = InputApi(db_config, brand_map)
        self.core = Core(db_config)

        try:
            self.logger = Logger("input", context.request)
            self.context.logger = self.logger
            self.logger.info("PIPELINE", "=== Pipeline started ===")
        except Exception as e:
            print("LOGGER CREATION FAILED:", str(e))
            raise

    def run(self):
        try:
            self.logger.info("PIPELINE", "Running parser")
            self._run_parser()

            self.logger.info("PIPELINE", "Running API")
            self._run_api()

            self.logger.info("PIPELINE", "Running CORE")
            self._run_core()

            self.logger.info("PIPELINE", "=== Pipeline finished ===")

            return {
                "status": "success"
                ## "errors": self.context.errors
            }

        except Exception as e:
            try:
                self.logger.error("PIPELINE", f"Fatal error: {str(e)}")
            except:
                print("LOGGER FAILED:", str(e))  # временно

            # self.context.add_error(str(e))
            # self.context.add_error(traceback.format_exc())

            return {
                "status": "error"
                # "errors": self.context.errors
            }


    # =========================================================
    # STAGES
    # =========================================================

    def _run_parser(self):
        parser_config = self.context.request.get("parser")

        self.logger.info("PARSER", "Running parser...")
        # print("[PIPELINE] Running parser...")

        data = ParserAdapter.run(parser_config, self.context)

        if data is None:
            self.logger.error("PARSER", "Parser failed or returned invalid data")
            raise Exception("Parser stage failed")
        elif isinstance(data, list):
            self.logger.info("PARSER", f"Parser returned batch: {len(data)} items")
        elif isinstance(data, dict):
            self.logger.info("PARSER", "Parser returned single object")
        else:
            self.logger.warning("PARSER", f"Unexpected data type: {type(data)}")
        # print(f"[DEBUG] Parser output: {type(data)} | {data}")                              # debag

        self.context.set_data(data)


    def _run_api(self):
        self.logger.info("API", "Running API...")
        # print("[PIPELINE] Running API...")

        data = self.context.data

        result = self.api.process(data)

        processed = result.get("data")
        meta = result.get("meta", {})

        skipped = meta.get("skipped", 0)
        if skipped > 0:
            self.logger.warning("API", f"Skipped items: {skipped}")

        if processed is None:
            self.logger.warning("API", "API returned None")
        elif isinstance(processed, list):
            self.logger.info("API", f"Processed batch: {len(processed)} items")
        elif isinstance(processed, dict):
            self.logger.info("API", "Processed single object")
        # print(f"[DEBUG] API output: {type(processed)} | {processed}")                       # debag

        self.context.set_data(processed)


    def _run_core(self):
        self.logger.info("CORE", "Running CORE...")
        # print("[PIPELINE] Running CORE...")

# ---------------------------------------------------------------- аналог print(f"[CORE] ads_id={ads_id}, check_id={check_id}, has_changes={has_changes}")
        data = self.context.data
        db_path = self.context.request.get("dbPath")

        if not data:
            self.logger.warning("CORE", "No data to save")
            return

        try:
            results = self.core.save(data, db_path)

            for item in results:
                self.logger.info(
                    "CORE",
                    f"ads_id={item['ads_id']}, "
                    f"check_id={item['check_id']}, "
                    f"has_changes={item['has_changes']}"
                )

        except Exception as e:
            self.logger.error("CORE", f"DB error: {str(e)}")
# ---------------------------------------------------------------- Блок ниже можно просто удалить (раньше состояние выводилось в print сейчас в logger)

#        data = self.context.data
#        db_path = self.context.request.get("dbPath")
#
#        if not data:
#            print("[PIPELINE] No data to save")
#            return
#
#        try:
#            self.core.save(data, db_path)
#
#            if isinstance(data, list):
#                self.logger.info("CORE", f"Saved batch: {len(data)} items")
#            else:
#                self.logger.info("CORE", "Saved single object")
#
#        except Exception as e:
#            self.logger.error("CORE", f"DB error: {str(e)}")

# =========================================================
# Entry Point
# =========================================================

def main():
    # try:

        input_json = sys.stdin.read()
        request = json.loads(input_json)

        context = PipelineContext(request)
        pipeline = InputPipelineManager(context)

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