from pydantic_settings import BaseSettings


class Settings(BaseSettings):
    sqlite_path: str = "/data/spans.db"
    min_cases_for_mining: int = 3

    model_config = {"env_prefix": "PM_"}


settings = Settings()
