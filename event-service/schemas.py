from marshmallow import Schema, fields, validate


class EventIn(Schema):
    player_id = fields.Str(required=True, validate=validate.Length(min=1))
    type = fields.Str(required=True, validate=validate.Length(min=1))
    payload = fields.Dict(keys=fields.Str(), values=fields.Raw(), load_default=dict)
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
