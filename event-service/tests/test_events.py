import os
import sys
import threading

import pytest

sys.path.insert(0, os.path.dirname(os.path.dirname(__file__)))
import app as app_module


@pytest.fixture
def client():
    app_module._events.clear()
    app_module._event_ids.clear()
    return app_module.create_app().test_client()


def post(client, game="g1", player="p1", type_="LeverToggled", **extra):
    body = {"player_id": player, "type": type_, "payload": {"x": 1, "y": 0, "z": 2}}
    body.update(extra)
    return client.post(f"/games/{game}/events", json=body)


def test_post_assigns_consecutive_seq(client):
    for expected in (1, 2, 3):
        r = post(client)
        assert r.status_code == 201
        assert r.get_json()["seq"] == expected
        assert r.get_json()["timestamp"]


def test_seq_is_independent_per_game(client):
    post(client, game="a")
    post(client, game="a")
    assert post(client, game="b").get_json()["seq"] == 1


def test_get_empty_game(client):
    r = client.get("/games/nuevo/events?since=0")
    assert r.status_code == 200
    assert r.get_json() == {"events": [], "last_seq": 0}


def test_get_without_since_returns_everything(client):
    post(client)
    post(client)
    assert len(client.get("/games/g1/events").get_json()["events"]) == 2


def test_since_filters_and_keeps_order(client):
    for t in ("A", "B", "C", "D"):
        post(client, type_=t)
    body = client.get("/games/g1/events?since=2").get_json()
    assert [e["seq"] for e in body["events"]] == [3, 4]
    assert [e["type"] for e in body["events"]] == ["C", "D"]
    assert body["last_seq"] == 4


def test_since_equal_to_last_seq_returns_nothing(client):
    post(client)
    body = client.get("/games/g1/events?since=1").get_json()
    assert body == {"events": [], "last_seq": 1}


def test_event_content_is_returned(client):
    post(client, player="p7", type_="DoorOpened")
    e = client.get("/games/g1/events?since=0").get_json()["events"][0]
    assert e["player_id"] == "p7"
    assert e["type"] == "DoorOpened"
    assert e["payload"] == {"x": 1, "y": 0, "z": 2}
    assert e["seq"] == 1 and e["timestamp"]


@pytest.mark.parametrize("missing", ["player_id", "type"])
def test_missing_required_field_is_rejected(client, missing):
    body = {"player_id": "p1", "type": "X", "payload": {}}
    del body[missing]
    assert client.post("/games/g1/events", json=body).status_code == 422
    assert client.get("/games/g1/events").get_json()["last_seq"] == 0


def test_empty_type_is_rejected(client):
    assert post(client, type_="").status_code == 422


def test_payload_is_optional(client):
    r = client.post("/games/g1/events", json={"player_id": "p1", "type": "X"})
    assert r.status_code == 201
    assert client.get("/games/g1/events").get_json()["events"][0]["payload"] == {}


def test_negative_since_is_rejected(client):
    assert client.get("/games/g1/events?since=-1").status_code == 422


def test_non_numeric_since_is_rejected(client):
    assert client.get("/games/g1/events?since=abc").status_code == 422


def test_resend_with_same_event_id_is_not_duplicated(client):
    first = post(client, event_id="abc").get_json()
    again = post(client, event_id="abc")
    assert again.status_code == 200
    assert again.get_json()["seq"] == first["seq"] == 1
    assert client.get("/games/g1/events").get_json()["last_seq"] == 1


def test_different_event_ids_are_both_registered(client):
    post(client, event_id="a")
    assert post(client, event_id="b").get_json()["seq"] == 2


def test_concurrent_posts_get_unique_consecutive_seq(client):
    n = 200
    seqs = []

    def worker():
        seqs.append(post(client).get_json()["seq"])

    threads = [threading.Thread(target=worker) for _ in range(n)]
    for t in threads:
        t.start()
    for t in threads:
        t.join()
    assert sorted(seqs) == list(range(1, n + 1))


def test_incremental_polling_receives_each_event_once(client):
    """Simula al cliente: guarda last_seq y lo manda como since."""
    received, since = [], 0
    for i in range(20):
        post(client, type_=f"E{i}")
        if i % 7 == 6:
            body = client.get(f"/games/g1/events?since={since}").get_json()
            received += [e["seq"] for e in body["events"]]
            since = body["events"][-1]["seq"]
    body = client.get(f"/games/g1/events?since={since}").get_json()
    received += [e["seq"] for e in body["events"]]
    assert received == list(range(1, 21))
