import json


class Api:

    def __init__(self, config_path=None):
        self.config = None

        ## if config_path:
        ##     self._load_config(config_path)


    ## =========================================================
    ## CONFIG
    ## =========================================================

    ## def _load_config(self, config_path):
    ##     with open(config_path, "r", encoding="utf-8") as f:
    ##         self.config = json.load(f)


    # =========================================================
    # MAIN
    # =========================================================

    def process(self, data):
        if data is None:
            return None

        # базовая нормализация
        normalized = self._normalize(data)

        ## normalized = self._apply_config(normalized)

        return normalized


    # =========================================================
    # NORMALIZE (простая логика)
    # =========================================================

    def _normalize(self, data):

        result = {}

        for key, value in data.items():

            # убираем пустые строки
            if value == "":
                value = None

            # пробуем привести числа
            value = self._try_cast_number(value)

            # чистим строки
            if isinstance(value, str):
                value = value.strip()

            result[key] = value

        return result


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


    ## =========================================================
    ## APPLY CONFIG (будущее расширение)
    ## =========================================================

    ## def _apply_config(self, data):
    ##     if not self.config:
    ##         return data

    ##     # пример: фильтруем только поля из ads_snapshots
    ##     allowed_fields = set(self.config.get("ads_snapshots", {}).keys())

    ##     result = {}
    ##     for key, value in data.items():
    ##         if key in allowed_fields or key in ("url", "source"):
    ##             result[key] = value

    ##     return result