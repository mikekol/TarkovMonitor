"""Tests for scav cooldown calculation."""

from __future__ import annotations

import pytest
from tarkovmonitor_tui.cooldown import calculate_scav_cooldown
from tarkovmonitor_tui.tarkov_dev import (
    HideoutStationBonus,
    HideoutStationLevel,
    TarkovHideoutStation,
    TraderReputationLevel,
)


def _fence_levels() -> list[TraderReputationLevel]:
    return [
        TraderReputationLevel(minimum_reputation=-7.0, scav_cooldown_modifier=1.5),
        TraderReputationLevel(minimum_reputation=-0.02, scav_cooldown_modifier=1.0),
        TraderReputationLevel(minimum_reputation=6.0, scav_cooldown_modifier=0.5),
    ]


def _air_filter_station() -> TarkovHideoutStation:
    return TarkovHideoutStation(
        id="s1",
        name="Air Filtering Unit",
        normalized_name="air-filtering-unit",
        levels=[
            HideoutStationLevel(
                id="lvl-1",
                level=1,
                bonuses=[HideoutStationBonus(type="ScavCooldownTimer", value=-0.05)],
            ),
            HideoutStationLevel(
                id="lvl-2",
                level=2,
                bonuses=[HideoutStationBonus(type="ScavCooldownTimer", value=-0.1)],
            ),
        ],
    )


def test_no_hideout_default_karma():
    # base=1500, karma=0.0 → modifier=1.0, hideout=0 → 1500
    result = calculate_scav_cooldown(
        base_seconds=1500,
        fence_rep_levels=_fence_levels(),
        karma=0.0,
        hideout_stations=[],
        built_level_ids=set(),
    )
    assert result == 1500


def test_high_karma_halves_cooldown():
    # base=1500, karma=6.0 → modifier=0.5, hideout=0 → round(750) = 750
    result = calculate_scav_cooldown(
        base_seconds=1500,
        fence_rep_levels=_fence_levels(),
        karma=6.0,
        hideout_stations=[],
        built_level_ids=set(),
    )
    assert result == 750


def test_low_karma_increases_cooldown():
    # base=1500, karma=-7.0 → modifier=1.5, hideout=0 → round(2250) = 2250
    result = calculate_scav_cooldown(
        base_seconds=1500,
        fence_rep_levels=_fence_levels(),
        karma=-7.0,
        hideout_stations=[],
        built_level_ids=set(),
    )
    assert result == 2250


def test_hideout_level1_reduces_cooldown():
    # base=1500, karma=0 → modifier=1.0, hideout=0.05 → round(1500 - 75) = 1425
    result = calculate_scav_cooldown(
        base_seconds=1500,
        fence_rep_levels=_fence_levels(),
        karma=0.0,
        hideout_stations=[_air_filter_station()],
        built_level_ids={"lvl-1"},
    )
    assert result == 1425


def test_hideout_level2_reduces_more():
    # base=1500, karma=0 → modifier=1.0, hideout=0.05+0.1=0.15 → round(1500 - 225) = 1275
    result = calculate_scav_cooldown(
        base_seconds=1500,
        fence_rep_levels=_fence_levels(),
        karma=0.0,
        hideout_stations=[_air_filter_station()],
        built_level_ids={"lvl-1", "lvl-2"},
    )
    assert result == 1275


def test_combined_karma_and_hideout():
    # base=1500, karma=6.0 → modifier=0.5, base_effective=750
    # hideout=0.05 → round(750 - 37.5) = round(712.5) = 712 (Python banker's rounding)
    # but formula: round(base * karma_bonus - base * karma_bonus * hideout_bonus)
    # = round(750 - 750*0.05) = round(750 - 37.5) = round(712.5) = 712
    result = calculate_scav_cooldown(
        base_seconds=1500,
        fence_rep_levels=_fence_levels(),
        karma=6.0,
        hideout_stations=[_air_filter_station()],
        built_level_ids={"lvl-1"},
    )
    assert result == round(750 - 750 * 0.05)


def test_empty_fence_levels_defaults_modifier_1():
    result = calculate_scav_cooldown(
        base_seconds=1500,
        fence_rep_levels=[],
        karma=99.0,
        hideout_stations=[],
        built_level_ids=set(),
    )
    assert result == 1500


def test_unbuilt_levels_not_counted():
    result = calculate_scav_cooldown(
        base_seconds=1500,
        fence_rep_levels=_fence_levels(),
        karma=0.0,
        hideout_stations=[_air_filter_station()],
        built_level_ids=set(),  # nothing built
    )
    assert result == 1500


def test_non_cooldown_bonuses_ignored():
    station = TarkovHideoutStation(
        id="s2",
        name="Other",
        normalized_name="other",
        levels=[
            HideoutStationLevel(
                id="other-lvl",
                level=1,
                bonuses=[
                    HideoutStationBonus(type="HealthRegeneration", value=-0.5),
                    HideoutStationBonus(type="ScavCooldownTimer", value=-0.05),
                ],
            )
        ],
    )
    result = calculate_scav_cooldown(
        base_seconds=1500,
        fence_rep_levels=_fence_levels(),
        karma=0.0,
        hideout_stations=[station],
        built_level_ids={"other-lvl"},
    )
    assert result == 1425  # only ScavCooldownTimer counted
