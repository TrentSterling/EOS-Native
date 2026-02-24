# EOS Stats Interface Reference

> Source: [dev.epicgames.com](https://dev.epicgames.com/docs/epic-online-services/player-and-game-data/stats-interface/stats-reference)

## Overview

The Stats service tracks player statistics for achievements, leaderboards, and custom game functionality.

## System Limits

| Feature | Limit |
|---------|-------|
| Stats per deployment | **500** |
| Stat name length | **256 characters** |
| Max stats ingested per call | **3,000** |
| Max milestones (default) | **100** |

## Rate Limits

| Operation | Per-User | Per-Deployment |
|-----------|----------|----------------|
| Ingest stats | 60/min, 500 stats/request | 1 req per 5 Client IDs/min |
| Get stats by Player ID | 100/min | - |
| Get stats by Player IDs | 100/min, 64 players/req, 25 stats/player | - |
| Create stat | - | 100/min |
| Delete stat | - | 100/min |

> When a dedicated server submits stats on behalf of users, per-user limits do not apply. Limits are based on rate per second for each clientId per deployment.

## Testing

Before deploying live, include stats in a sandbox deployment to:
- Check for errors in stat structure
- Verify players can unlock achievements
- Identify configuration issues before public release
