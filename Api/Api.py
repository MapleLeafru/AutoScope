import json


class Api:

    # =========================================================
    # ALLOWED FIELDS (контракт с БД)
    # =========================================================

    ALLOWED_FIELDS = {
        # ads
        "source",
        "url",

        # ads_snapshots
        "brand",
        "model",
        "price",
        "year",
        "brand_origin_country",
        "sale_region",
        "license_plate",
        "mileage",
        "transmission",
        "drive_type",
        "color",
        "body_type",
        "steering_wheel",
        "engine_power",
        "engine_volume",
        "engine_model",
        "fuel_type",
        "octane",
        "powertrain_type",
        "description"
    }

    REQUIRED_FIELDS = {
        "url"
    }

    # =========================================================
    # INIT
    # =========================================================

    def __init__(self, config_path=None):
        self.config = None

        ## if config_path:
        ##     self._load_config(config_path)


    # =========================================================
    # CONFIG
    # =========================================================

    ## def _load_config(self, config_path):
    ##     with open(config_path, "r", encoding="utf-8") as f:
    ##         self.config = json.load(f)


    # =========================================================
    # MAIN
    # =========================================================

    def process(self, data):
        if data is None:
            return None

        # список объектов
        if isinstance(data, list):
            result = []

            for item in data:
                normalized = self._normalize(item)

                ## if normalized is None:
                ##     continue

                result.append(normalized)

            ## result = self._apply_config(result)
            return result

        # один объект
        normalized = self._normalize(data)

        ## normalized = self._apply_config(normalized)
        return normalized


    # =========================================================
    # NORMALIZE
    # =========================================================

    def _normalize(self, data):

        if not isinstance(data, dict):
            return None

        result = {}

        for key, value in data.items():

            # фильтрация полей
            if key not in self.ALLOWED_FIELDS:
                continue

            # пустые строки → None
            if value == "":
                value = None

            # чистка строк
            if isinstance(value, str):
                value = value.strip()

            # приведение чисел
            value = self._try_cast_number(value)

            result[key] = value

        # обязательные поля
        ## for field in self.REQUIRED_FIELDS:
        ##     if not result.get(field):
        ##         return None

        return result


    # =========================================================
    # CAST
    # =========================================================

    def _try_cast_number(self, value):

        if isinstance(value, str):

            # int
            if value.isdigit():
                return int(value)

            # float
            try:
                return float(value)
            except:
                pass

        return value


    # =========================================================
    # CONFIG APPLY (будущее)
    # =========================================================

    ## def _apply_config(self, data):
    ##     if not self.config:
    ##         return data

    ##     allowed_fields = set(self.config.get("ads_snapshots", {}).keys())

    ##     def filter_item(item):
    ##         result = {}
    ##         for key, value in item.items():
    ##             if key in allowed_fields or key in ("url", "source"):
    ##                 result[key] = value
    ##         return result

    ##     if isinstance(data, list):
    ##         return [filter_item(item) for item in data]

    ##     return filter_item(data)