"""
Pipeline configuration for Ryze collaborations
"""

import os
from dataclasses import dataclass, field

@dataclass
class SourceConfig:
    baseUrl: str = os.getenv("CLIENT_BASE_URL", "CLIENT_HTTP_URL")
    api_key: str = os.getenv("CLIENT_API_KEY", "")
    timeout: int = 30
    max_retries: int = 3
    retry_delay: float = 2
    batch_size: int = 50

@dataclass
class DestinationConfig:
    baseUrl: str = os.getenv("HELPCENTER_BASE_URL", "HELPCENTER_HTTP_URL")
    api_key: str = os.getenv("HELPCENTER_API_KEY", "")
    timeout: int = 30
    max_retries: int = 3
    retry_delay: float = 2

@dataclass
class PipelineConfig:
    source: SourceConfig = field(default_factory=SourceConfig)
    destination: DestinationConfig = field(default_factory=DestinationConfig)

    auto_categorize: bool = True
    auto_priority: bool = True
    filter_resolved: bool = True
    filter_closed: bool = True
    deduplicate: bool = True

    log_level: str = os.getenv("HELPCENTER_LOG_LEVEL", "INFO")

    # schedule
    poll_interval: int = 300