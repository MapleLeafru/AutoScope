import sys
import os
import json
import subprocess

ROOT_DIR = os.path.abspath(os.path.join(os.path.dirname(__file__), ".."))
sys.path.append(ROOT_DIR)

from Api.OutputApi import OutputApi
from Core.Core import Core
from Utils.Logger import Logger
from Utils.ConfigLoader import ConfigLoader

class AnalyzerAdapter:

    @staticmethod
    def run(analyzer_config, data, context):
        analyzer_path = analyzer_config.get("analyzerPath")
        python_path = analyzer_config.get("python", "python")

        if not analyzer_path or not os.path.exists(analyzer_path):
            context.logger.error("ANALYZER", f"Analyzer not found: {analyzer_path}")
            return None

        process = subprocess.Popen(
            [python_path, analyzer_path],
            stdin=subprocess.PIPE,
            stdout=subprocess.PIPE,
            stderr=subprocess.PIPE,
            text=True,
            encoding="utf-8"
        )

        try:
            stdout, stderr = process.communicate(
                json.dumps(data, ensure_ascii=False)
            )
        except Exception as e:
            context.logger.error("ANALYZER", f"Communicate failed: {str(e)}")
            raise

        if stderr:
            context.logger.error("ANALYZER", f"stderr: {stderr.strip()}")

        if not stdout:
            context.logger.warning("ANALYZER", "Empty output from analyzer")
            return None

        try:
            return json.loads(stdout)
        except Exception:
            return {"raw": stdout}

class OutputPipelineManager:

    def __init__(self, request):
        self.request = request

        config_path = request.get("configPath")
        db_path = request.get("dbPath")

        db_config = ConfigLoader.load_database_config(db_path)

        self.api = OutputApi(db_config)
        self.core = Core(db_config)

        self.logger = Logger("output", request)
        self.logger.info("PIPELINE", "=== Output Pipeline started ===")

    def run(self):
        try:
            # --- PREPARE ---
            self.logger.info("API", "Preparing query")
            query = self.api.prepare(self.request)

            # --- FETCH ---
            self.logger.info("CORE", "Fetching data")
            data = self.core.fetch_all(self.request.get("dbPath"))

            self.logger.info("CORE", f"Fetched records: {len(data)}")

            # --- PROCESS ---
            self.logger.info("API", "Processing data")
            result = self.api.process(data)

            # --- ANALYZER ---
            analyzer_config = self.request.get("analyzer")

            if not analyzer_config:
                self.logger.warning("ANALYZER", "No analyzer provided")
                return {"status": "no_analyzer"}

            self.logger.info("ANALYZER", "Running analyzer")

            analyzer_result = AnalyzerAdapter.run(
                analyzer_config,
                result,
                self
            )

            self.logger.info("PIPELINE", "=== Output Pipeline finished ===")

            return {
                "status": "success",
                "result": analyzer_result
            }

        except Exception as e:
            self.logger.error("PIPELINE", f"Fatal error: {str(e)}")
            return {"status": "error"}


# =========================================================
# Entry Point
# =========================================================

def main():
    input_json = sys.stdin.read()
    request = json.loads(input_json)

    pipeline = OutputPipelineManager(request)
    result = pipeline.run()

    print(json.dumps(result, ensure_ascii=False))


if __name__ == "__main__":
    main()