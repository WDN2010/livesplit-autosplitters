#!/usr/bin/env python3
"""Deterministic lifecycle and callback regressions for Nunholy_IGT.asl."""

from __future__ import annotations

from dataclasses import dataclass

EPSILON = 0.001


@dataclass
class Settings:
    auto_start: bool = True
    auto_reset: bool = True
    split_floors: bool = True
    split_final: bool = True
    start_enabled: bool = True
    split_enabled: bool = True
    reset_enabled: bool = True


@dataclass
class State:
    auto_start_armed: bool = False
    cold_process_proven: bool = False
    cold_start_pending: bool = False
    cold_observation_open: bool = True
    run_active: bool = False
    captured_start: float | None = None
    last_accepted_start: float | None = None
    expected_chapter: int = 1
    start_candidate: bool = False
    auto_start_attempt: bool = False
    manual_start_waiting: bool = False
    manual_start_rejected: bool = False
    reset_candidate: bool = False
    pending_split: int = 0  # 1 = floor, 2 = final
    finish_seen: bool = False
    frozen_igt: float | None = None
    was_paused: bool = False
    result_freeze_observed: bool = False
    fresh_sample: bool = False
    have_valid_sample: bool = False
    latest_scene: str = ""
    have_last_valid_sample: bool = False
    last_valid_chapter: int = 0
    last_valid_is_battle: bool = False
    timer_phase: str = "Running"


@dataclass(frozen=True)
class Sample:
    scene: str | None
    chapter: int
    raw_igt: float
    start_time: float
    target_vampire: int = 0
    time_scale: float = 1.0
    is_battle: bool = True
    valid: bool = True
    scene_valid: bool = True
    scene_address: int = 1
    recheck_scene: str | None = None
    recheck_scene_valid: bool = True
    recheck_scene_address: int | None = None


def bind_run(state: State, sample: Sample) -> None:
    state.auto_start_armed = False
    state.cold_process_proven = False
    state.cold_start_pending = False
    state.cold_observation_open = False
    state.run_active = True
    state.captured_start = sample.start_time
    state.last_accepted_start = sample.start_time
    state.expected_chapter = 1
    state.start_candidate = False
    state.auto_start_attempt = False
    state.manual_start_waiting = False
    state.manual_start_rejected = False
    state.pending_split = 0
    state.finish_seen = False


def update(
    state: State,
    settings: Settings,
    sample: Sample,
) -> float:
    # Pending work belongs to the sample that queued it. Every new poll starts
    # with no callback work, including polls that fail validation.
    state.pending_split = 0
    state.start_candidate = False
    state.reset_candidate = False
    state.have_valid_sample = False
    state.fresh_sample = False

    scene = sample.scene
    if (
        not sample.valid
        or scene is None
        or not scene
        or len(scene) > 200
        or sample.scene_address == 0
        or not sample.scene_valid
    ):
        return sample.raw_igt

    recheck_scene = scene if sample.recheck_scene is None else sample.recheck_scene
    recheck_address = (
        sample.scene_address
        if sample.recheck_scene_address is None
        else sample.recheck_scene_address
    )
    if (
        not sample.recheck_scene_valid
        or recheck_scene is None
        or not recheck_scene
        or len(recheck_scene) > 200
        or recheck_address == 0
        or recheck_scene != scene
        or recheck_address != sample.scene_address
    ):
        return sample.raw_igt

    if scene != "Battle":
        state.auto_start_armed = True

    first_cold_observation = state.cold_observation_open
    state.cold_observation_open = False
    cold_phase_eligible = state.timer_phase in {"NotRunning", "Ended"}
    cold_zero_observation = (
        cold_phase_eligible
        and first_cold_observation
        and scene == "Battle"
        and sample.chapter == 0
        and sample.target_vampire == 0
        and sample.start_time == 0.0
    )
    if cold_zero_observation:
        state.cold_start_pending = True
    elif (not cold_phase_eligible or scene != "Battle"
          or sample.chapter != 0 or sample.target_vampire != 0):
        state.cold_start_pending = False
        state.cold_observation_open = False
        state.cold_process_proven = False

    replaced = (
        state.run_active
        and state.captured_start is not None
        and abs(sample.start_time - state.captured_start) > EPSILON
    )
    if replaced:
        # The old freeze/result latches belong to a different accepted identity.
        state.frozen_igt = None
        state.was_paused = False
        state.result_freeze_observed = False

    paused = 0.0 <= sample.time_scale <= 0.0001
    if state.run_active and paused and not replaced:
        if not state.was_paused or state.frozen_igt is None:
            state.frozen_igt = sample.raw_igt
        effective_igt = state.frozen_igt
    else:
        effective_igt = sample.raw_igt
        state.frozen_igt = sample.raw_igt

    state.result_freeze_observed = sample.time_scale == 0.0 and state.run_active
    state.was_paused = paused

    left_run = state.run_active and scene != "Battle"
    state.reset_candidate = replaced or left_run

    fresh = scene == "Battle" and sample.chapter == 0 and 0.0 <= effective_igt <= 10.0
    classic = sample.target_vampire == 0
    state.fresh_sample = fresh and classic
    state.have_valid_sample = True
    state.latest_scene = scene

    if state.manual_start_waiting and state.fresh_sample:
        bind_run(state, sample)
        state.reset_candidate = False

    if replaced and scene == "Battle" and state.fresh_sample:
        state.auto_start_armed = True

    unseen = (
        state.last_accepted_start is None
        or abs(sample.start_time - state.last_accepted_start) > EPSILON
    )
    cold_settled_identity = (
        scene == "Battle"
        and sample.chapter == 0
        and sample.target_vampire == 0
        and sample.start_time > 0.0
    )
    cold_identity_observation = cold_settled_identity and (
        state.cold_start_pending
        or (state.cold_process_proven and first_cold_observation)
    )
    if cold_identity_observation:
        state.cold_process_proven = False
        state.cold_start_pending = False
        state.cold_observation_open = False
    fresh_auto_start_observation = (
        state.auto_start_armed and state.fresh_sample and unseen
    )
    cold_auto_start_observation = (
        cold_identity_observation
        and cold_phase_eligible
        and state.fresh_sample
        and unseen
    )
    fresh_auto_start_observation = (
        fresh_auto_start_observation or cold_auto_start_observation
    )
    state.start_candidate = (
        settings.auto_start
        and settings.start_enabled
        and fresh_auto_start_observation
        and not state.manual_start_waiting
    )
    if fresh_auto_start_observation:
        state.auto_start_armed = False
        state.cold_process_proven = False
        state.cold_start_pending = False
        state.cold_observation_open = False

    same_run = (
        state.run_active
        and scene == "Battle"
        and state.captured_start is not None
        and abs(sample.start_time - state.captured_start) <= EPSILON
    )
    timer_running = state.timer_phase == "Running"
    if same_run:
        expected = state.expected_chapter
        entered_next_floor = (
            1 <= expected <= 4
            and state.have_last_valid_sample
            and state.last_valid_chapter == expected - 1
            and sample.chapter == expected
        )
        if entered_next_floor:
            state.expected_chapter = expected + 1
            if timer_running and settings.split_enabled and settings.split_floors:
                state.pending_split = 1

        final_result = (
            state.expected_chapter == 5
            and sample.chapter == 4
            and state.have_last_valid_sample
            and state.last_valid_is_battle
            and not sample.is_battle
            and sample.time_scale == 0.0
            and state.result_freeze_observed
        )
        if final_result:
            state.finish_seen = True
            if timer_running and settings.split_enabled and settings.split_final:
                state.pending_split = 2

    state.have_last_valid_sample = True
    state.last_valid_chapter = sample.chapter
    state.last_valid_is_battle = sample.is_battle
    return float(effective_igt)


def start_action(state: State, settings: Settings) -> bool:
    if not settings.auto_start or not settings.start_enabled or not state.start_candidate:
        return False
    state.auto_start_attempt = True
    return True


def on_start(state: State, settings: Settings, sample: Sample | None, *, automatic: bool) -> None:
    if automatic:
        state.auto_start_attempt = False
        if sample is not None and state.start_candidate and state.fresh_sample:
            bind_run(state, sample)
        return

    state.start_candidate = False
    state.auto_start_armed = False
    state.cold_process_proven = False
    state.cold_start_pending = False
    state.cold_observation_open = False
    if not state.have_valid_sample or state.latest_scene != "Battle":
        state.manual_start_waiting = True
    elif state.fresh_sample and sample is not None:
        bind_run(state, sample)
    else:
        state.manual_start_rejected = True


def split_action(state: State, settings: Settings) -> bool:
    if not state.run_active or not settings.split_enabled:
        state.pending_split = 0
        return False
    if state.pending_split == 1 and not settings.split_floors:
        state.pending_split = 0
        return False
    if state.pending_split == 2 and not settings.split_final:
        state.pending_split = 0
        return False
    return state.pending_split in (1, 2)


def on_split(state: State) -> None:
    state.pending_split = 0


def on_reset(state: State) -> None:
    state.run_active = False
    state.cold_process_proven = False
    state.cold_start_pending = False
    state.cold_observation_open = False
    state.captured_start = None
    state.last_accepted_start = None
    state.expected_chapter = 1
    state.reset_candidate = False
    state.auto_start_attempt = False
    state.manual_start_waiting = False
    state.manual_start_rejected = False
    state.pending_split = 0
    state.frozen_igt = None
    state.was_paused = False
    state.result_freeze_observed = False
    state.have_last_valid_sample = False
    state.last_valid_chapter = 0
    state.last_valid_is_battle = False
    state.start_candidate = False


def exit_action(settings: Settings, phase: str) -> bool:
    # Process identity is gone at exit, so this safety cleanup is deliberately
    # independent of ordinary custom/base Reset controls.
    return phase in {"Running", "Paused"}


def main() -> None:
    # The base Start checkbox gates the predicate, and start{} only records an
    # attempt. The accepted run is committed by onStart, not start{}.
    settings = Settings(start_enabled=False)
    state = State(auto_start_armed=True)
    update(state, settings, Sample("Battle", 0, 0.2, 100.0))
    assert not state.start_candidate
    assert not start_action(state, settings)
    assert not state.run_active

    settings.start_enabled = True
    update(state, settings, Sample("Battle", 0, 0.3, 100.0))
    assert not state.start_candidate
    assert not start_action(state, settings)
    assert not state.run_active

    state = State(auto_start_armed=True)
    update(state, settings, Sample("Battle", 0, 0.2, 101.0))
    assert state.start_candidate
    assert start_action(state, settings)
    assert not state.run_active
    on_start(state, settings, Sample("Battle", 0, 0.2, 101.0), automatic=True)
    assert state.run_active
    print("PASS: disabled Start consumes fresh observation without replay")

    # Cold attach recovery requires either an observed zero/unsettled identity
    # or an init-only new-process proof. A zero sample is held even when native
    # Time.time is already beyond the normal ten-second freshness window.
    for raw_igt in (0.2, 15.0):
        state = State(timer_phase="NotRunning")
        sample = Sample("Battle", 0, raw_igt, 0.0)
        update(state, Settings(), sample)
        assert state.cold_start_pending and not state.start_candidate
        settled = Sample("Battle", 0, 0.2, 20.0)
        update(state, Settings(), settled)
        assert state.start_candidate
        assert start_action(state, Settings())
        on_start(state, Settings(), settled, automatic=True)
        assert state.run_active
    print("PASS: cold zero-to-settled recovery accepts native time below/above ten")

    state = State(timer_phase="NotRunning")
    update(state, Settings(), Sample("Battle", 0, 0.2, 0.0))
    update(state, Settings(), Sample("Battle", 0, 0.2, 20.0, valid=False))
    assert state.cold_start_pending and not state.start_candidate
    update(state, Settings(), Sample("Battle", 0, 0.2, 20.0))
    assert state.start_candidate
    print("PASS: invalid gap preserves cold pending evidence")

    cancelled = State(timer_phase="NotRunning")
    update(cancelled, Settings(), Sample("Battle", 0, 15.0, 0.0))
    on_reset(cancelled)
    update(cancelled, Settings(), Sample("Battle", 0, 15.0, 0.0))
    update(cancelled, Settings(), Sample("Battle", 0, 0.2, 20.0))
    assert not cancelled.start_candidate
    for diversion in (Sample("Battle", 1, 20.0, 20.0), Sample("Battle", 0, 0.2, 20.0, target_vampire=1)):
        cancelled = State(timer_phase="NotRunning")
        update(cancelled, Settings(), Sample("Battle", 0, 15.0, 0.0))
        update(cancelled, Settings(), diversion)
        update(cancelled, Settings(), Sample("Battle", 0, 0.2, 100.0))
        assert not cancelled.start_candidate
    print("PASS: reset/higher-chapter/non-Classic cannot reuse cold evidence")

    state = State(timer_phase="NotRunning", cold_process_proven=True)
    update(state, Settings(), Sample("Battle", 0, 0.2, 20.0))
    assert state.start_candidate
    print("PASS: proven new process accepts first settled fresh Battle")

    state = State(timer_phase="NotRunning")
    update(state, Settings(), Sample("Battle", 0, 0.2, 20.0))
    assert not state.start_candidate and not state.cold_start_pending
    print("PASS: existing-process settled Battle remains unarmed")

    state = State(timer_phase="NotRunning")
    update(state, Settings(), Sample("Battle", 0, 0.2, 0.0))
    update(state, Settings(), Sample("Battle", 0, 11.0, 20.0))
    assert not state.start_candidate and not state.cold_start_pending
    update(state, Settings(), Sample("Battle", 0, 0.2, 20.0))
    assert not state.start_candidate
    print("PASS: settled late IGT consumes cold evidence without starting")

    disabled = Settings(start_enabled=False)
    state = State(timer_phase="NotRunning")
    update(state, disabled, Sample("Battle", 0, 0.2, 0.0))
    update(state, disabled, Sample("Battle", 0, 0.2, 30.0))
    assert not state.start_candidate and not state.cold_start_pending
    disabled.start_enabled = True
    update(state, disabled, Sample("Battle", 0, 0.3, 30.0))
    assert not state.start_candidate
    print("PASS: disabled Start consumes cold observation without replay")

    paused = State(timer_phase="Paused")
    update(paused, Settings(), Sample("Battle", 0, 0.2, 0.0))
    assert not paused.cold_start_pending
    ended = State(timer_phase="Ended", cold_process_proven=True)
    update(ended, Settings(), Sample("Battle", 0, 0.2, 40.0))
    assert ended.start_candidate
    on_reset(ended)
    assert not ended.cold_process_proven and not ended.cold_start_pending
    print("PASS: paused/Ended cold phase guards and reset clear evidence")

    state = State(timer_phase="NotRunning", cold_start_pending=True, cold_process_proven=True)
    on_start(state, Settings(), None, automatic=False)
    assert state.manual_start_waiting and not state.cold_start_pending
    print("PASS: manual start consumes cold evidence")

    state = State(timer_phase="NotRunning")
    update(state, Settings(), Sample("Title", 0, 4.0, 0.0, is_battle=False))
    assert state.auto_start_armed
    update(state, Settings(), Sample("Battle", 0, 0.2, 0.0))
    assert state.start_candidate
    print("PASS: proven menu preserves legitimate zero startTime warm path")

    # Manual start from a menu waits for the first fresh Classic/Any% Battle
    # sample and does not manufacture an auto-reset candidate while waiting.
    state = State(auto_start_armed=True)
    menu = Sample("Title", 0, 4.0, 1.0, is_battle=False)
    update(state, settings, menu)
    on_start(state, settings, menu, automatic=False)
    assert state.manual_start_waiting and not state.reset_candidate
    update(state, settings, Sample("Battle", 0, 0.2, 101.0))
    assert state.run_active and not state.reset_candidate
    print("PASS: manual menu start binds first fresh Battle sample")

    # Manual Start is safe even before the process/helper has produced a sample.
    state = State()
    on_start(state, settings, None, automatic=False)
    assert state.manual_start_waiting and not state.run_active
    update(state, settings, Sample("Battle", 0, 0.1, 150.0))
    assert state.run_active
    print("PASS: manual pre-attach start waits without dynamic current access")

    # Manual start on a fresh Battle sample binds immediately; a mid-run manual
    # start remains unsupported without poisoning run/reset latches.
    state = State(auto_start_armed=True)
    fresh = Sample("Battle", 0, 0.2, 200.0)
    update(state, settings, fresh)
    on_start(state, settings, fresh, automatic=False)
    assert state.run_active

    state = State()
    mid_run = Sample("Battle", 2, 40.0, 300.0)
    update(state, settings, mid_run)
    on_start(state, settings, mid_run, automatic=False)
    assert state.manual_start_rejected and not state.run_active
    update(state, settings, Sample("Title", 2, 40.0, 300.0, is_battle=False))
    assert not state.reset_candidate
    print("PASS: manual fresh-Battle bind and fail-closed mid-run start")

    # A same-scene restart changes the accepted run identity while the old run
    # is paused. The new run must use its raw IGT, not the stale frozen latch.
    state = State(
        auto_start_armed=False,
        run_active=True,
        captured_start=100.0,
        last_accepted_start=100.0,
        frozen_igt=40.0,
        was_paused=True,
    )
    restarted = Sample("Battle", 0, 0.2, 200.0, time_scale=0.0)
    effective = update(state, Settings(), restarted)
    assert effective == restarted.raw_igt
    assert state.fresh_sample and state.reset_candidate and state.start_candidate
    print("PASS: paused same-scene restart discards stale frozen IGT")

    # Base Split disabled consumes observations in update but never emits them,
    # and re-enabling it cannot emit a retroactive floor/final split.
    state = State(
        run_active=True,
        captured_start=400.0,
        last_accepted_start=400.0,
        have_last_valid_sample=True,
        last_valid_chapter=0,
        last_valid_is_battle=True,
    )
    disabled = Settings(split_enabled=False)
    update(state, disabled, Sample("Battle", 1, 20.0, 400.0))
    assert state.expected_chapter == 2 and state.pending_split == 0
    disabled.split_enabled = True
    disabled.split_floors = True
    update(state, disabled, Sample("Battle", 1, 20.1, 400.0))
    assert state.pending_split == 0
    state.expected_chapter = 5
    state.have_last_valid_sample = True
    state.last_valid_chapter = 4
    state.last_valid_is_battle = True
    state.frozen_igt = 30.0
    disabled.split_enabled = False
    update(
        state,
        disabled,
        Sample("Battle", 4, 30.0, 400.0, is_battle=False, time_scale=0.0),
    )
    assert state.finish_seen and state.pending_split == 0
    disabled.split_enabled = True
    assert not split_action(state, disabled)
    print("PASS: base/custom Split controls prevent retroactive emission")

    # A queued split is discarded by the next invalid poll, while run identity
    # and the last complete source sample remain durable.
    state = State(
        run_active=True,
        captured_start=425.0,
        last_accepted_start=425.0,
        have_last_valid_sample=True,
        last_valid_chapter=0,
        last_valid_is_battle=True,
    )
    enabled = Settings()
    update(state, enabled, Sample("Battle", 1, 10.0, 425.0))
    assert state.pending_split == 1 and state.expected_chapter == 2
    update(state, enabled, Sample("Battle", 1, 10.1, 425.0, valid=False))
    assert (
        state.pending_split == 0
        and state.run_active
        and state.last_valid_chapter == 1
    )
    assert not split_action(state, enabled)
    print("PASS: invalid poll clears stale pending split")

    # Scene identity is required before a sample can arm auto-start or update
    # durable observations; an identity change during reads is also invalid.
    for bad_scene in ("", None, "x" * 201):
        state = State()
        update(state, enabled, Sample(bad_scene, 0, 4.0, 426.0, is_battle=False))
        assert not state.have_valid_sample and not state.auto_start_armed
        update(state, enabled, Sample("Battle", 0, 0.2, 426.0))
        assert state.have_valid_sample and not state.start_candidate

    state = State()
    update(
        state,
        enabled,
        Sample("Title", 0, 4.0, 427.0, is_battle=False, scene_valid=False),
    )
    assert not state.have_valid_sample and not state.auto_start_armed

    state = State(
        run_active=True,
        captured_start=428.0,
        last_accepted_start=428.0,
        have_last_valid_sample=True,
        last_valid_chapter=0,
        last_valid_is_battle=True,
    )
    update(
        state,
        enabled,
        Sample("Battle", 1, 10.0, 428.0, recheck_scene="ChangedDuringRead"),
    )
    assert (
        not state.have_valid_sample
        and state.expected_chapter == 1
        and state.last_valid_chapter == 0
        and state.run_active
    )
    update(state, enabled, Sample("Battle", 1, 10.1, 428.0))
    assert state.pending_split == 1
    print("PASS: scene guards fail closed without poisoning source")

    # Paused and Ended observations advance dedupe state but cannot publish
    # split work that a later Running sample could replay.
    state = State(
        timer_phase="Paused",
        run_active=True,
        captured_start=430.0,
        last_accepted_start=430.0,
        have_last_valid_sample=True,
        last_valid_chapter=0,
        last_valid_is_battle=True,
    )
    update(state, enabled, Sample("Battle", 1, 10.0, 430.0))
    assert state.expected_chapter == 2 and state.pending_split == 0
    state.timer_phase = "Running"
    update(state, enabled, Sample("Battle", 1, 10.1, 430.0))
    assert state.pending_split == 0

    state = State(
        timer_phase="Ended",
        run_active=True,
        captured_start=431.0,
        last_accepted_start=431.0,
        expected_chapter=5,
        have_last_valid_sample=True,
        last_valid_chapter=4,
        last_valid_is_battle=True,
    )
    update(
        state,
        enabled,
        Sample("Battle", 4, 40.0, 431.0, is_battle=False, time_scale=0.0),
    )
    assert state.finish_seen and state.pending_split == 0
    print("PASS: paused/Ended observations are consumed without split queue")

    # Invalid native polls do not advance the durable last-valid sample. Each
    # floor level and the final result must survive a destination gap.
    state = State(
        run_active=True,
        captured_start=450.0,
        last_accepted_start=450.0,
        have_last_valid_sample=True,
        last_valid_chapter=0,
        last_valid_is_battle=True,
    )
    enabled = Settings()
    for chapter in range(1, 5):
        update(
            state,
            enabled,
            Sample("Battle", chapter, chapter * 10.0, 450.0, valid=False),
        )
        assert state.expected_chapter == chapter and state.pending_split == 0
        update(state, enabled, Sample("Battle", chapter, chapter * 10.1, 450.0))
        assert state.pending_split == 1
        assert split_action(state, enabled)
        on_split(state)
        assert state.expected_chapter == chapter + 1

    update(
        state,
        enabled,
        Sample("Battle", 4, 40.0, 450.0, is_battle=False, time_scale=0.0, valid=False),
    )
    assert state.expected_chapter == 5 and not state.finish_seen
    update(
        state,
        enabled,
        Sample("Battle", 4, 40.1, 450.0, is_battle=False, time_scale=0.0),
    )
    assert state.finish_seen and split_action(state, enabled)
    on_split(state)
    print("PASS: all floors and final survive invalid destination gaps")

    # The final result edge is only accepted after chapter/order/same-run,
    # last-valid isBattle -> false, and exact-zero result-freeze evidence.
    state = State(
        run_active=True,
        captured_start=500.0,
        last_accepted_start=500.0,
        expected_chapter=5,
        frozen_igt=30.0,
        have_last_valid_sample=True,
        last_valid_chapter=4,
        last_valid_is_battle=True,
    )
    enabled = Settings()
    update(
        state,
        enabled,
        # ResultCanvas and isBattle change in one Unity frame. The first
        # zero-scale poll must split even if IGT advanced since the prior poll.
        Sample("Battle", 4, 30.02, 500.0, is_battle=False, time_scale=0.0),
    )
    assert state.finish_seen and split_action(state, enabled)
    on_split(state)

    state = State(
        run_active=True,
        captured_start=501.0,
        last_accepted_start=501.0,
        expected_chapter=5,
        frozen_igt=30.0,
        have_last_valid_sample=True,
        last_valid_chapter=4,
        last_valid_is_battle=True,
    )
    update(state, enabled, Sample("Battle", 4, 30.0, 501.0))
    update(
        state,
        enabled,
        Sample("Battle", 4, 30.0, 501.0, is_battle=False, time_scale=0.01),
    )
    assert not state.finish_seen and not split_action(state, enabled)
    print("PASS: final split requires exact-zero result freeze")

    # Ordinary reset clears the accepted identity. A new run with an equal
    # startTime must not be rejected as a duplicate after that reset.
    state = State(
        auto_start_armed=True,
        run_active=True,
        captured_start=700.0,
        last_accepted_start=700.0,
    )
    update(state, enabled, Sample("Title", 0, 4.0, 700.0, is_battle=False))
    assert state.reset_candidate and state.auto_start_armed
    on_reset(state)
    assert state.last_accepted_start is None
    update(state, enabled, Sample("Battle", 0, 0.2, 700.0))
    assert state.start_candidate
    print("PASS: ordinary reset clears identity for equal-start fresh run")

    # Process exit resets every incomplete phase regardless of controls, while
    # preserving a completed result.
    for phase in ("Running", "Paused"):
        assert exit_action(Settings(), phase)
        assert exit_action(Settings(auto_reset=False), phase)
        assert exit_action(Settings(reset_enabled=False), phase)
        assert exit_action(Settings(auto_reset=False, reset_enabled=False), phase)
    assert not exit_action(Settings(), "Ended")
    assert not exit_action(Settings(auto_reset=False), "Ended")
    assert not exit_action(Settings(reset_enabled=False), "Ended")
    print("PASS: process-exit reset policy ignores controls and preserves Ended")


if __name__ == "__main__":
    main()
