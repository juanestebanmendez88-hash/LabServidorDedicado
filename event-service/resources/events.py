import threading
from datetime import datetime, timezone

from flask.views import MethodView
from flask_smorest import Blueprint

from schemas import AckOut, EventIn, EventsOut, SinceQuery

blp = Blueprint("events", __name__, description="Servicio de eventos")

_events = {}
_event_ids = {}
_lock = threading.Lock()


@blp.route("/games/<string:game_id>/events")
class Events(MethodView):
    @blp.arguments(EventIn, location="json")
    @blp.response(201, AckOut)
    def post(self, data, game_id):
        """Registra un evento y devuelve el seq asignado."""
        event_id = data["event_id"] or None
        with _lock:
            log = _events.setdefault(game_id, [])
            ids = _event_ids.setdefault(game_id, {})

            if event_id is not None and event_id in ids:
                original = log[ids[event_id] - 1]
                return {"seq": original["seq"], "timestamp": original["timestamp"]}, 200

            event = {
                "seq": len(log) + 1,
                "player_id": data["player_id"],
                "type": data["type"],
                "payload": data["payload"],
                "timestamp": datetime.now(timezone.utc).isoformat(),
            }
            log.append(event)
            if event_id is not None:
                ids[event_id] = event["seq"]

        return {"seq": event["seq"], "timestamp": event["timestamp"]}

    @blp.arguments(SinceQuery, location="query")
    @blp.response(200, EventsOut)
    def get(self, args, game_id):
        """Devuelve, en orden, los eventos con seq mayor que since."""
        with _lock:
            log = _events.get(game_id, [])
            return {"events": list(log[args["since"]:]), "last_seq": len(log)}
