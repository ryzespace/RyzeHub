import logging


#conf logger

LOG_FORMAT = '%(asctime)s - %(levelname)s - %(message)s'
LOG_LEVEL = logging.INFO

# set  settings
DEFAULT_HUB_DIR = "github_hub"
GITHUB_API_URL = "https://api.github.com"


DOCKER_TEMPLATES = {
    'python': {
        'image': "python:3.9-slim",
        'cmd': 'RUN pip install -r requirements.txt || echo "No requirements.txt found"\nCMD ["python", "main.py"]'
    },
    'nodejs': {
        'image': "node:16-slim",
        'cmd': 'RUN npm install || echo "No package.json found"\nCMD ["npm", "start"]'
    },
    '.NET': {
        'image': ".net:8-slim",
        'cmd': 'RUN cs build -o program .\nCMD ["./program"]'
    },
    'generic': {
        'image': "ubuntu:latest",
        'cmd': 'CMD ["sh"]'
    }
}

