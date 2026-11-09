const dbRef = db.getSiblingDB("library_nosql_lab");

dbRef.createCollection("catalog_items", {
  validator: {
    $jsonSchema: {
      bsonType: "object",
      required: [
        "_id",
        "schemaVersion",
        "code",
        "itemType",
        "title",
        "publicationYear",
        "languageCode",
        "publisher",
        "genre",
        "replacementCostMinor",
        "availableCopies",
        "popularityScore",
        "isActive",
        "createdAt",
        "contributors",
        "contents",
        "tags",
        "specs"
      ],
      properties: {
        _id: { bsonType: "string" },
        schemaVersion: { bsonType: "int", minimum: 1 },
        code: { bsonType: "string" },
        itemType: { enum: ["printed_book", "audiobook", "periodical", "board_game"] },
        title: { bsonType: "string" },
        publicationYear: { bsonType: "int", minimum: 1400, maximum: 2030 },
        languageCode: { bsonType: "string" },
        publisher: {
          bsonType: "object",
          required: ["id", "name", "country"],
          properties: {
            id: { bsonType: "int" },
            name: { bsonType: "string" },
            country: { bsonType: "string" },
            website: { bsonType: ["string", "null"] }
          }
        },
        genre: {
          bsonType: "object",
          required: ["id", "code", "name"],
          properties: {
            id: { bsonType: "int" },
            code: { bsonType: "string" },
            name: { bsonType: "string" }
          }
        },
        replacementCostMinor: { bsonType: "int", minimum: 0 },
        availableCopies: { bsonType: "int", minimum: 0 },
        popularityScore: { bsonType: "int", minimum: 0 },
        isActive: { bsonType: "bool" },
        createdAt: { bsonType: "date" },
        contributors: {
          bsonType: "array",
          minItems: 1,
          maxItems: 10,
          items: {
            bsonType: "object",
            required: ["id", "fullName", "role", "displayOrder"],
            properties: {
              id: { bsonType: "int" },
              fullName: { bsonType: "string" },
              country: { bsonType: "string" },
              role: { enum: ["author", "narrator", "translator", "editor", "designer"] },
              displayOrder: { bsonType: "int" }
            }
          }
        },
        contents: {
          bsonType: "array",
          minItems: 1,
          maxItems: 25,
          items: {
            bsonType: "object",
            required: ["position", "title"],
            properties: {
              position: { bsonType: "int" },
              title: { bsonType: "string" },
              durationSec: { bsonType: ["int", "null"] },
              pageOrQty: { bsonType: ["int", "null"] }
            }
          }
        },
        tags: {
          bsonType: "array",
          minItems: 1,
          maxItems: 15,
          items: { bsonType: "string" }
        },
        specs: { bsonType: "object" }
      }
    }
  }
});

dbRef.catalog_items.createIndex({ code: 1 }, { unique: true, name: "ux_code" });
dbRef.catalog_items.createIndex(
  { isActive: 1, itemType: 1, "genre.id": 1, replacementCostMinor: 1, _id: 1 },
  { name: "ix_active_type_genre_cost" }
);
dbRef.catalog_items.createIndex(
  { isActive: 1, itemType: 1, popularityScore: -1, _id: 1, "specs.minPlayers": 1, "specs.maxPlayers": 1, "specs.playTimeMin": 1 },
  { name: "ix_boardgame_specs_popularity" }
);
dbRef.catalog_items.createIndex(
  { isActive: 1, "contributors.id": 1, "contributors.role": 1, publicationYear: -1, _id: 1 },
  { name: "ix_contributors_elem" }
);
dbRef.catalog_items.createIndex(
  { "publisher.id": 1 },
  { name: "ix_publisher_id" }
);
dbRef.catalog_items.createIndex(
  { isActive: 1, "genre.code": 1, itemType: 1 },
  { name: "ix_genre_type_agg" }
);
