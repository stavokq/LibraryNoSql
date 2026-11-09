CREATE INDEX idx_catalog_items_type_genre_cost
ON catalog_items USING btree (item_type, genre_id, replacement_cost_minor, id)
WHERE is_active = TRUE;

CREATE INDEX idx_catalog_items_type_popularity
ON catalog_items USING btree (item_type, popularity_score DESC, id)
WHERE is_active = TRUE;

CREATE INDEX idx_catalog_items_publisher
ON catalog_items USING btree (publisher_id);

CREATE INDEX idx_catalog_items_genre
ON catalog_items USING btree (genre_id);

CREATE INDEX idx_item_contributors_contributor_role
ON catalog_item_contributors USING btree (contributor_id, role, item_id);

CREATE INDEX idx_item_contents_item_pos
ON catalog_item_contents USING btree (item_id, position);

CREATE INDEX idx_item_tags_tag_item
ON catalog_item_tags USING btree (tag_id, item_id);

CREATE INDEX idx_item_attrs_key_int
ON catalog_item_attributes USING btree (attr_key, val_int, item_id)
WHERE val_int IS NOT NULL;

CREATE INDEX idx_item_attrs_item_key_int
ON catalog_item_attributes USING btree (item_id, attr_key, val_int)
WHERE val_int IS NOT NULL;

CREATE INDEX idx_item_attrs_key_str
ON catalog_item_attributes USING btree (attr_key, val_string, item_id)
WHERE val_string IS NOT NULL;

CREATE INDEX idx_item_attrs_key_bool
ON catalog_item_attributes USING btree (attr_key, val_bool, item_id)
WHERE val_bool IS NOT NULL;
