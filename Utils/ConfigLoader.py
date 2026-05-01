import os
import json


class ConfigLoader:

    # =========================================================
    # PATHS
    # =========================================================

    ROOT_DIR = os.path.abspath(os.path.join(os.path.dirname(__file__), ".."))
    CONFIGS_DIR = os.path.join(ROOT_DIR, "Configs")
    DB_CONFIGS_DIR = os.path.join(CONFIGS_DIR, "DataBaseConfigs")

    BASE_DB_CONFIG = "BaseDataBaseConfig.json"
    BASE_DB_RESTORE = "BaseDataBaseConfig.restor.json"

    # =========================================================
    # UNIVERSAL CONFIG LOAD
    # =========================================================

    @staticmethod
    def load_config(config_name):
        path = os.path.join(ConfigLoader.CONFIGS_DIR, config_name)
        return ConfigLoader.load_json(path)

    # =========================================================
    # DATABASE CONFIG AUTO LOAD
    # =========================================================

    @staticmethod
    def load_database_config(db_path):

        db_name = os.path.basename(db_path)

        parts = db_name.split(".")

        # ожидаем format: name.ConfigName.db
        if len(parts) >= 3:
            config_name = parts[-2] + ".json"
        else:
            config_name = ConfigLoader.BASE_DB_CONFIG

        config_path = os.path.join(
            ConfigLoader.DB_CONFIGS_DIR,
            config_name
        )

        if os.path.exists(config_path):
            return ConfigLoader.load_json(config_path)

        # fallback на базовый
        base_path = os.path.join(
            ConfigLoader.DB_CONFIGS_DIR,
            ConfigLoader.BASE_DB_CONFIG
        )

        try:
            return ConfigLoader.load_json(base_path)

        except Exception as e:
            raise Exception(
                "BaseDataBaseConfig is missing or corrupted. "
                "Please restore it from BaseDataBaseConfig.restor.json"
            )

    # =========================================================
    # JSON LOAD
    # =========================================================

    @staticmethod
    def load_json(path):
        with open(path, "r", encoding="utf-8") as f:
            return json.load(f)

    # =========================================================
    # RESTORE LOAD (SAFE, MEMORY ONLY)
    # =========================================================

    @staticmethod
    def load_base_restore():

        restore_path = os.path.join(
            ConfigLoader.CONFIGS_DIR,
            ConfigLoader.BASE_DB_RESTORE
        )

        return ConfigLoader.load_json(restore_path)

    @staticmethod
    def load_brand_country_map(configs_path):
        path = os.path.join(configs_path, "BrandCountryMap.json")
        return ConfigLoader.load_json(path)

