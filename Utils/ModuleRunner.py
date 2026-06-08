# -*- coding: utf-8 -*-
import os
import json
import shutil
import subprocess
import threading


class ModuleRunner:
    # Универсальный запускатель внешних модулей AutoScope: Python, Java JAR и exe.

    EXTENSION_TO_RUNTIME = {
        ".py": "python",
        ".jar": "java",
        ".exe": "exe",
    }

    DEFAULT_EXECUTABLES = {
        "python": "python",
        "java": "java",
    }

    @staticmethod
    def run_streaming(module_config, request, logger=None, stage="MODULE"):
        # Запускает парсер и построчно возвращает JSON-объекты из stdout.
        command, cwd = ModuleRunner._build_command(module_config, request)
        ModuleRunner._log(logger, stage, "info", f"Starting module: {' '.join(command)}")

        try:
            process = subprocess.Popen(
                command,
                stdin=subprocess.PIPE,
                stdout=subprocess.PIPE,
                stderr=subprocess.PIPE,
                text=True,
                encoding="utf-8",
                errors="replace",
                bufsize=1,
                cwd=cwd,
                env=ModuleRunner._build_env(),
            )
        except FileNotFoundError as e:
            raise RuntimeError(f"Runtime executable was not found: {command[0]}") from e

        stderr_thread = ModuleRunner._start_stderr_reader(process, logger, stage)

        try:
            process.stdin.write(json.dumps(request, ensure_ascii=False))
            process.stdin.close()
        except Exception as e:
            ModuleRunner._safe_kill(process)
            raise RuntimeError(f"Failed to send input to module: {e}") from e

        emitted_count = 0

        for line in process.stdout:
            line = line.strip()
            if not line:
                continue

            try:
                payload = json.loads(line)
                emitted_count += 1
                yield payload
            except json.JSONDecodeError:
                ModuleRunner._log(
                    logger,
                    stage,
                    "error",
                    "Invalid stdout JSON line. Module must write logs to stderr. "
                    f"Line: {line[:500]}",
                )

        exit_code = process.wait()
        stderr_thread.join(timeout=2)

        if emitted_count == 0:
            ModuleRunner._log(logger, stage, "warning", "Module produced no JSON output")

        if exit_code != 0:
            raise RuntimeError(f"Module finished with non-zero exit code: {exit_code}")

    @staticmethod
    def run_once(module_config, input_payload, request, logger=None, stage="MODULE"):
        # Запускает анализатор и читает из stdout один JSON-результат.
        command, cwd = ModuleRunner._build_command(module_config, request)
        ModuleRunner._log(logger, stage, "info", f"Starting module: {' '.join(command)}")

        try:
            process = subprocess.Popen(
                command,
                stdin=subprocess.PIPE,
                stdout=subprocess.PIPE,
                stderr=subprocess.PIPE,
                text=True,
                encoding="utf-8",
                errors="replace",
                cwd=cwd,
                env=ModuleRunner._build_env(),
            )
        except FileNotFoundError as e:
            raise RuntimeError(f"Runtime executable was not found: {command[0]}") from e

        stdout, stderr = process.communicate(json.dumps(input_payload, ensure_ascii=False))

        if stderr:
            for line in stderr.splitlines():
                if line.strip():
                    ModuleRunner._log(logger, stage, "error", f"stderr: {line.strip()}")

        if process.returncode != 0:
            raise RuntimeError(f"Module finished with non-zero exit code: {process.returncode}")

        stdout = (stdout or "").strip()
        if not stdout:
            ModuleRunner._log(logger, stage, "warning", "Module produced empty stdout")
            return None

        try:
            return json.loads(stdout)
        except json.JSONDecodeError:
            ModuleRunner._log(logger, stage, "warning", "Module returned non-JSON stdout; raw output saved")
            return {"raw": stdout}

    @staticmethod
    def _build_command(module_config, request):
        # Формирует команду запуска по типу внешнего модуля.
        module_path = ModuleRunner._get_module_path(module_config)
        if not module_path:
            raise RuntimeError("Module path is missing")

        if not os.path.exists(module_path):
            raise FileNotFoundError(f"Module file not found: {module_path}")

        runtime = ModuleRunner._get_runtime(module_config, module_path)
        runtime_settings = request.get("runtimeSettings", {}) or {}

        if runtime == "python":
            python_path = (
                module_config.get("python")
                or module_config.get("pythonPath")
                or runtime_settings.get("pythonPath")
                or ModuleRunner.DEFAULT_EXECUTABLES["python"]
            )
            python_path = ModuleRunner._resolve_executable(python_path, "python")
            return [python_path, module_path], ModuleRunner._get_working_directory(module_config, module_path)

        if runtime == "java":
            if os.path.splitext(module_path)[1].lower() != ".jar":
                raise RuntimeError("Java modules must be compiled into .jar files")

            java_path = (
                module_config.get("java")
                or module_config.get("javaPath")
                or runtime_settings.get("javaPath")
                or ModuleRunner.DEFAULT_EXECUTABLES["java"]
            )
            java_path = ModuleRunner._resolve_executable(java_path, "java")
            return [java_path, "-Dfile.encoding=UTF-8", "-jar", module_path], ModuleRunner._get_working_directory(module_config, module_path)

        if runtime == "exe":
            return [module_path], ModuleRunner._get_working_directory(module_config, module_path)

        raise RuntimeError(f"Unsupported module runtime: {runtime}")

    @staticmethod
    def _get_module_path(module_config):
        # Получает путь до модуля из parser/analyzer-конфига.
        if not module_config:
            return None

        return (
            module_config.get("modulePath")
            or module_config.get("parserPath")
            or module_config.get("analyzerPath")
        )

    @staticmethod
    def _get_runtime(module_config, module_path):
        # Определяет runtime явно из конфига или по расширению файла.
        runtime = (module_config or {}).get("runtime")
        if runtime:
            return str(runtime).lower().strip()

        ext = os.path.splitext(module_path)[1].lower()
        runtime = ModuleRunner.EXTENSION_TO_RUNTIME.get(ext)
        if not runtime:
            raise RuntimeError(f"Unsupported module extension: {ext}")

        return runtime

    @staticmethod
    def _resolve_executable(value, runtime_name):
        # Проверяет runtime: явный путь до файла или команда, доступная через PATH.
        value = (value or "").strip()

        if not value or value.lower() in ["auto", "default", "path"]:
            value = ModuleRunner.DEFAULT_EXECUTABLES.get(runtime_name, runtime_name)

        if os.path.isabs(value) or os.path.dirname(value):
            if os.path.exists(value):
                return value
            raise RuntimeError(f"Configured {runtime_name} executable was not found: {value}")

        found = shutil.which(value)
        if found:
            return found

        if runtime_name == "java":
            raise RuntimeError(
                "Java runtime was not found. Install Java and add it to PATH, "
                "or set javaPath in Configs/ParserDefaultSettings.json"
            )

        if runtime_name == "python":
            raise RuntimeError(
                "Python runtime was not found. Set pythonPath in Configs/ParserDefaultSettings.json "
                "or check the bundled Python folder"
            )

        raise RuntimeError(f"Runtime executable was not found: {runtime_name}")

    @staticmethod
    def _get_working_directory(module_config, module_path):
        # Возвращает рабочую папку модуля.
        working_directory = (module_config or {}).get("workingDirectory")
        if working_directory:
            return working_directory

        return os.path.dirname(os.path.abspath(module_path))

    @staticmethod
    def _build_env():
        # Готовит переменные окружения для корректной UTF-8 работы Python-модулей.
        env = os.environ.copy()
        env["PYTHONIOENCODING"] = "utf-8"
        return env

    @staticmethod
    def _start_stderr_reader(process, logger, stage):
        # Асинхронно читает stderr парсера во время streaming-запуска.
        def reader():
            for line in process.stderr:
                line = line.strip()
                if line:
                    ModuleRunner._log(logger, stage, "error", f"stderr: {line}")

        thread = threading.Thread(target=reader, daemon=True)
        thread.start()
        return thread

    @staticmethod
    def _safe_kill(process):
        # Пытается остановить внешний процесс при ошибке передачи stdin.
        try:
            process.kill()
        except Exception:
            pass

    @staticmethod
    def _log(logger, stage, level, message):
        # Пишет сообщение в логгер, если логгер был передан.
        if not logger:
            return

        method = getattr(logger, level, None)
        if method:
            method(stage, message)
