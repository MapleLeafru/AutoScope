class OutputApi:

    def __init__(self, db_config=None):
        self.db_config = db_config or {}

    # =========================================================
    # PREPARE QUERY (пока просто заглушка)
    # =========================================================

    def prepare(self, request):
        # в будущем тут будут фильтры
        return {
            "type": "all"
        }

    # =========================================================
    # PROCESS DATA
    # =========================================================

    def process(self, data):
        if not data:
            return {
                "data": [],
                "meta": {"count": 0}
            }

        # пока просто passthrough
        return {
            "data": data,
            "meta": {
                "count": len(data)
            }
        }