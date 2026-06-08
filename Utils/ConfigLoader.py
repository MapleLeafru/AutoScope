# -*- coding: utf-8 -*-
import os
import json


class ConfigLoader:
    # Центральный загрузчик JSON-конфигов и справочников AutoScope.

    ROOT_DIR = os.path.abspath(os.path.join(os.path.dirname(__file__), ".."))
    CONFIGS_DIR = os.path.join(ROOT_DIR, "Configs")
    DB_CONFIGS_DIR = os.path.join(CONFIGS_DIR, "DataBaseConfigs")
    DICTIONARIES_DIR = os.path.join(CONFIGS_DIR, "Dictionaries")

    BASE_DB_CONFIG = "BaseDataBaseConfig.json"
    BASE_DB_RESTORE = "BaseDataBaseConfig.restor.json"

    @staticmethod
    def load_config(config_name):
        # Загружает JSON-конфиг из папки Configs по имени файла.
        path = os.path.join(ConfigLoader.CONFIGS_DIR, config_name)
        return ConfigLoader.load_json(path)

    @staticmethod
    def load_database_config(db_path):
        # Определяет конфиг БД по имени файла базы и загружает его из DataBaseConfigs.
        db_name = os.path.basename(db_path)
        parts = db_name.split(".")

        if len(parts) >= 3:
            config_name = parts[-2] + ".json"
        else:
            config_name = ConfigLoader.BASE_DB_CONFIG

        config_path = os.path.join(ConfigLoader.DB_CONFIGS_DIR, config_name)
        if os.path.exists(config_path):
            return ConfigLoader.load_json(config_path)

        base_path = os.path.join(ConfigLoader.DB_CONFIGS_DIR, ConfigLoader.BASE_DB_CONFIG)
        try:
            return ConfigLoader.load_json(base_path)
        except Exception as e:
            raise RuntimeError(
                "BaseDataBaseConfig is missing or corrupted. "
                "Please restore it from BaseDataBaseConfig.restor.json"
            ) from e

    @staticmethod
    def load_json(path):
        # Читает JSON-файл в UTF-8 и возвращает объект Python.
        with open(path, "r", encoding="utf-8") as f:
            return json.load(f)

    @staticmethod
    def load_base_restore():
        # Загружает резервную копию базового конфига базы данных.
        restore_path = os.path.join(ConfigLoader.DB_CONFIGS_DIR, ConfigLoader.BASE_DB_RESTORE)
        if os.path.exists(restore_path):
            return ConfigLoader.load_json(restore_path)

        old_restore_path = os.path.join(ConfigLoader.CONFIGS_DIR, ConfigLoader.BASE_DB_RESTORE)
        return ConfigLoader.load_json(old_restore_path)

    @staticmethod
    def load_dictionary(configs_path, dictionary_name):
        # Загружает справочник из Configs/Dictionaries с fallback на старый путь.
        base_configs_path = configs_path or ConfigLoader.CONFIGS_DIR

        new_path = os.path.join(base_configs_path, "Dictionaries", dictionary_name)
        if os.path.exists(new_path):
            return ConfigLoader.load_json(new_path)

        old_path = os.path.join(base_configs_path, dictionary_name)
        if os.path.exists(old_path):
            return ConfigLoader.load_json(old_path)

        default_new_path = os.path.join(ConfigLoader.DICTIONARIES_DIR, dictionary_name)
        if os.path.exists(default_new_path):
            return ConfigLoader.load_json(default_new_path)

        raise FileNotFoundError(f"Dictionary file not found: {dictionary_name}")

    @staticmethod
    def load_brand_country_map(configs_path=None):
        # Загружает справочник стран происхождения брендов.
        return ConfigLoader.load_dictionary(configs_path, "BrandCountryMap.json")

    @staticmethod
    def load_logger_config():
        # Загружает настройки логгера или возвращает безопасные значения по умолчанию.
        path = os.path.join(ConfigLoader.CONFIGS_DIR, "LoggerConfig.json")

        if not os.path.exists(path):
            return {"retention_days": 7}

        return ConfigLoader.load_json(path)
