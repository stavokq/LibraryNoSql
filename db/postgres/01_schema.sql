DROP TABLE IF EXISTS catalog_item_attributes CASCADE;
DROP TABLE IF EXISTS catalog_item_tags CASCADE;
DROP TABLE IF EXISTS catalog_item_contents CASCADE;
DROP TABLE IF EXISTS catalog_item_contributors CASCADE;
DROP TABLE IF EXISTS catalog_items CASCADE;
DROP TABLE IF EXISTS tags CASCADE;
DROP TABLE IF EXISTS contributors CASCADE;
DROP TABLE IF EXISTS genres CASCADE;
DROP TABLE IF EXISTS publishers CASCADE;

CREATE TABLE publishers (
    id INT PRIMARY KEY,
    name VARCHAR(150) NOT NULL UNIQUE,
    country VARCHAR(100) NOT NULL,
    website VARCHAR(150) NULL
);

CREATE TABLE genres (
    id INT PRIMARY KEY,
    code VARCHAR(50) NOT NULL UNIQUE,
    name VARCHAR(100) NOT NULL
);

CREATE TABLE contributors (
    id INT PRIMARY KEY,
    full_name VARCHAR(150) NOT NULL,
    country VARCHAR(100) NOT NULL
);

CREATE TABLE tags (
    id INT PRIMARY KEY,
    name VARCHAR(60) NOT NULL UNIQUE
);

CREATE TABLE catalog_items (
    id VARCHAR(36) PRIMARY KEY,
    code VARCHAR(40) NOT NULL UNIQUE,
    item_type VARCHAR(30) NOT NULL CHECK (item_type IN ('printed_book', 'audiobook', 'periodical', 'board_game')),
    title VARCHAR(255) NOT NULL,
    publication_year INT NOT NULL CHECK (publication_year >= 1400 AND publication_year <= 2030),
    language_code VARCHAR(10) NOT NULL,
    publisher_id INT NOT NULL REFERENCES publishers(id),
    genre_id INT NOT NULL REFERENCES genres(id),
    replacement_cost_minor INT NOT NULL CHECK (replacement_cost_minor >= 0),
    available_copies INT NOT NULL DEFAULT 0 CHECK (available_copies >= 0),
    popularity_score INT NOT NULL DEFAULT 0 CHECK (popularity_score >= 0),
    is_active BOOLEAN NOT NULL DEFAULT TRUE,
    created_at TIMESTAMP WITH TIME ZONE NOT NULL
);

CREATE TABLE catalog_item_contributors (
    item_id VARCHAR(36) NOT NULL REFERENCES catalog_items(id) ON DELETE CASCADE,
    contributor_id INT NOT NULL REFERENCES contributors(id),
    role VARCHAR(40) NOT NULL CHECK (role IN ('author', 'narrator', 'translator', 'editor', 'designer')),
    display_order SMALLINT NOT NULL,
    PRIMARY KEY (item_id, contributor_id, role)
);

CREATE TABLE catalog_item_contents (
    id BIGSERIAL PRIMARY KEY,
    item_id VARCHAR(36) NOT NULL REFERENCES catalog_items(id) ON DELETE CASCADE,
    position SMALLINT NOT NULL,
    title VARCHAR(200) NOT NULL,
    duration_sec INT NULL,
    page_or_qty INT NULL
);

CREATE TABLE catalog_item_tags (
    item_id VARCHAR(36) NOT NULL REFERENCES catalog_items(id) ON DELETE CASCADE,
    tag_id INT NOT NULL REFERENCES tags(id),
    PRIMARY KEY (item_id, tag_id)
);

CREATE TABLE catalog_item_attributes (
    item_id VARCHAR(36) NOT NULL REFERENCES catalog_items(id) ON DELETE CASCADE,
    attr_key VARCHAR(50) NOT NULL,
    val_string VARCHAR(150) NULL,
    val_int INT NULL,
    val_bool BOOLEAN NULL,
    PRIMARY KEY (item_id, attr_key)
);
