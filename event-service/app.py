import threading
from datetime import datetime, timezone

from flask import Flask
from flask.views import MethodView
from flask_smorest import Api, Blueprint
from marshmallow import Schema, fields, validate

blp = Blueprint("events", __name__, description="Servicio de eventos")

# game_id -> lista de eventos. El evento con seq=n está en el índice n-1,
# así "eventos con seq > since" es simplemente events[since:].
_events = {}
# game_id -> {event_id: seq}, para que un reenvío no registre el evento dos veces.
_event_ids = {}
# Un solo candado: asignar el seq y guardar el evento debe ser atómico, porque
# el servidor atiende varias peticiones a la vez.
_lock = threading.Lock()


class EventIn(Schema):
    player_id = fields.Str(required=True, validate=validate.Length(min=1))
    type = fields.Str(required=True, validate=validate.Length(min=1))
    payload = fields.Dict(keys=fields.Str(), values=fields.Raw(), load_default=dict)
    # Opcional: identificador generado por el cliente para evitar duplicados al reenviar.
    event_id = fields.Str(load_default=None)


class EventOut(Schema):
    seq = fields.Int()
    player_id = fields.Str()
    type = fields.Str()
    payload = fields.Dict()
    timestamp = fields.Str()


class AckOut(Schema):
    seq = fields.Int()
    timestamp = fields.Str()


class EventsOut(Schema):
    events = fields.List(fields.Nested(EventOut))
    last_seq = fields.Int()


class SinceQuery(Schema):
    since = fields.Int(load_default=0, validate=validate.Range(min=0))


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
                # Reenvío de un evento ya registrado: se devuelve el mismo seq.
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


def create_app():
    app = Flask(__name__)
    app.config["PROPAGATE_EXCEPTIONS"] = True
    app.config["API_TITLE"] = "Servicio de eventos"
    app.config["API_VERSION"] = "v1"
    app.config["OPENAPI_VERSION"] = "3.0.3"
    app.config["OPENAPI_URL_PREFIX"] = "/"
    app.config["OPENAPI_SWAGGER_UI_PATH"] = "/swagger-ui"
    app.config["OPENAPI_SWAGGER_UI_URL"] = "https://cdn.jsdelivr.net/npm/swagger-ui-dist/"

    api = Api(app)
    api.register_blueprint(blp)
    return app
