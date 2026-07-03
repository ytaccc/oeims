package com.oeims.config

import com.oeims.models.*
import io.ktor.server.application.*
import org.jetbrains.exposed.sql.Database
import org.jetbrains.exposed.sql.SchemaUtils
import org.jetbrains.exposed.sql.transactions.transaction

fun Application.configureDatabase() {
    val dbPath = environment.config.property("database.path").getString()

    Database.connect(
        url = "jdbc:sqlite:$dbPath",
        driver = "org.sqlite.JDBC"
    )

    // Create all tables if they don't exist yet.
    // Order matters: parent tables before child tables (FK constraints).
    transaction {
        SchemaUtils.create(
            Users,
            Exams,
            Sessions,
            SessionSupervisors,
            SessionJoins,
            Participants,
            Events
        )
    }

    log.info("Database initialised at $dbPath")
}
