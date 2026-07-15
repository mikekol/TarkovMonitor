"""Scav cooldown calculation matching the .NET GUI formula."""

from __future__ import annotations

from tarkovmonitor_tui.tarkov_dev import (
    TarkovHideoutStation,
    TraderReputationLevel,
)


def calculate_scav_cooldown(
    base_seconds: int,
    fence_rep_levels: list[TraderReputationLevel],
    karma: float,
    hideout_stations: list[TarkovHideoutStation],
    built_level_ids: set[str],
) -> int:
    """Return scav cooldown in seconds.

    Formula: round(base × karma_bonus − base × karma_bonus × hideout_bonus)
    karma_bonus: scavCooldownModifier from the highest matching Fence rep level
    hideout_bonus: sum of abs(value) for all built ScavCooldownTimer station bonuses
    """
    karma_bonus = 1.0
    matching = [r for r in fence_rep_levels if r.minimum_reputation <= karma]
    if matching:
        karma_bonus = matching[-1].scav_cooldown_modifier

    hideout_bonus = sum(
        abs(bonus.value)
        for station in hideout_stations
        for level in station.levels
        if level.id in built_level_ids
        for bonus in level.bonuses
        if bonus.type == "ScavCooldownTimer"
    )

    return round(base_seconds * karma_bonus - base_seconds * karma_bonus * hideout_bonus)
